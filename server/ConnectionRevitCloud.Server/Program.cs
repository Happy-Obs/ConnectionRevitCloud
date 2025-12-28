using System.Security.Claims;
using ConnectionRevitCloud.Server.Data;
using ConnectionRevitCloud.Server.Middleware;
using ConnectionRevitCloud.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Ensure local folders exist (when running from repo)
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "downloads"));

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Db")));

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<WireGuardService>();
builder.Services.AddScoped<UpdateService>();

// JWT auth
var jwtKey = builder.Configuration["Security:JwtKey"] ?? "CHANGE_ME";
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(opt =>
  {
      opt.TokenValidationParameters = new TokenValidationParameters
      {
          ValidateIssuer = true,
          ValidateAudience = true,
          ValidateIssuerSigningKey = true,
          ValidIssuer = builder.Configuration["Security:JwtIssuer"],
          ValidAudience = builder.Configuration["Security:JwtAudience"],
          IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
          ClockSkew = TimeSpan.FromSeconds(10)
      };
  });

builder.Services.AddAuthorization();

var app = builder.Build();

// Create DB without migrations (simple, reliable for v1)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Static /downloads (updates)
var downloadsDir = Path.Combine(app.Environment.ContentRootPath, "downloads");
Directory.CreateDirectory(downloadsDir);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(downloadsDir),
    RequestPath = "/downloads"
});

// Admin IP-only on /admin
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/admin"), adminApp =>
{
    adminApp.UseMiddleware<AdminIpOnlyMiddleware>();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "ConnectionRevitCloud server is running.");

record LoginReq(string Username, string Password);

// Client login
app.MapPost("/api/v1/login", async (LoginReq req, AuthService auth) =>
{
    var token = await auth.Login(req.Username, req.Password);
    return token is null ? Results.Unauthorized() : Results.Ok(new { token });
});

// Get config (enabled users only)
app.MapGet("/api/v1/config", async (ClaimsPrincipal user, AuthService auth, WireGuardService wg) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();

    var dbUser = await auth.GetUser(username);
    if (dbUser is null || !dbUser.IsEnabled) return Results.Unauthorized();

    var cfg = await wg.GetClientConfigText(dbUser);
    return Results.Text(cfg, "text/plain; charset=utf-8");
}).RequireAuthorization();

// Me
app.MapGet("/api/v1/me", async (ClaimsPrincipal user, AuthService auth) =>
{
    var username = user.Identity?.Name;
    if (string.IsNullOrWhiteSpace(username)) return Results.Unauthorized();

    var dbUser = await auth.GetUser(username);
    if (dbUser is null) return Results.Unauthorized();

    return Results.Ok(new { dbUser.Username, dbUser.WgIp, dbUser.IsEnabled });
}).RequireAuthorization();

// Updates
app.MapGet("/api/v1/client/latest", (UpdateService upd) => Results.Ok(upd.GetLatest()));

// ---------------- ADMIN (IP-only) ----------------
app.MapGet("/admin", async (AppDbContext db) =>
{
    var users = await db.Users.OrderBy(u => u.Username).ToListAsync();

    var html = $@"
<!doctype html><html><head><meta charset='utf-8'>
<title>ConnectionRevitCloud Admin</title>
<style>
body{{font-family:Segoe UI,Arial;padding:24px;}}
table{{border-collapse:collapse;width:100%;}}
td,th{{border:1px solid #ddd;padding:8px;}}
th{{background:#f3f3f3;}}
a.button{{padding:6px 10px;background:#0078D4;color:white;text-decoration:none;border-radius:6px;}}
</style></head><body>
<h2>ConnectionRevitCloud — Админка</h2>
<p><a class='button' href='/admin/new-existing'>+ Пользователь (привязать конфиг)</a>
<a class='button' href='/admin/new-generate'>+ Пользователь (сгенерировать конфиг)</a></p>

<table>
<tr><th>Логин</th><th>WG IP</th><th>Enabled</th><th>ConfigPath</th><th>Действия</th></tr>
{string.Join("", users.Select(u => $@"
<tr>
<td>{System.Net.WebUtility.HtmlEncode(u.Username)}</td>
<td>{System.Net.WebUtility.HtmlEncode(u.WgIp)}</td>
<td>{(u.IsEnabled ? "✅" : "⛔")}</td>
<td>{System.Net.WebUtility.HtmlEncode(u.ConfigPath)}</td>
<td>
<a href='/admin/toggle?u={Uri.EscapeDataString(u.Username)}'>Вкл/Выкл</a> |
<a href='/admin/delete?u={Uri.EscapeDataString(u.Username)}'>Удалить</a>
</td>
</tr>"))}
</table>
</body></html>";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/admin/new-existing", () =>
{
    var html = @"<!doctype html><html><head><meta charset='utf-8'><title>New</title></head>
<body style='font-family:Segoe UI;padding:24px;'>
<h3>Создать пользователя + привязать существующий конфиг</h3>
<form method='post' action='/admin/new-existing'>
<p>Логин: <input name='username' required></p>
<p>Пароль: <input name='password' required></p>
<p>WG IP (10.10.0.X): <input name='wgip' required></p>
<p>Путь к конфигу (на сервере): <input name='configPath' style='width:420px' required></p>
<p><button type='submit'>Создать</button> <a href='/admin'>Назад</a></p>
</form></body></html>";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/admin/new-existing", async (HttpRequest req, AuthService auth) =>
{
    var form = await req.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var wgip = form["wgip"].ToString().Trim();
    var configPath = form["configPath"].ToString().Trim();

    await auth.CreateUser(username, password, wgip, configPath);
    return Results.Redirect("/admin");
});

app.MapGet("/admin/new-generate", () =>
{
    var html = @"<!doctype html><html><head><meta charset='utf-8'><title>New</title></head>
<body style='font-family:Segoe UI;padding:24px;'>
<h3>Создать пользователя + сгенерировать конфиг + добавить peer на сервер</h3>
<form method='post' action='/admin/new-generate'>
<p>Логин: <input name='username' required></p>
<p>Пароль: <input name='password' required></p>
<p>WG IP (10.10.0.X): <input name='wgip' required></p>
<p><button type='submit'>Создать</button> <a href='/admin'>Назад</a></p>
</form></body></html>";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/admin/new-generate", async (HttpRequest req, WireGuardService wg) =>
{
    var form = await req.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var wgip = form["wgip"].ToString().Trim();

    await wg.CreateUserWithGeneratedConfig(username, password, wgip);
    return Results.Redirect("/admin");
});

app.MapGet("/admin/toggle", async (string u, AuthService auth) =>
{
    await auth.ToggleUser(u);
    return Results.Redirect("/admin");
});

app.MapGet("/admin/delete", (string u) =>
{
    var html = $@"<!doctype html><html><head><meta charset='utf-8'><title>Delete</title></head>
<body style='font-family:Segoe UI;padding:24px;'>
<h3>Удалить пользователя {System.Net.WebUtility.HtmlEncode(u)}?</h3>
<form method='post' action='/admin/delete'>
<input type='hidden' name='u' value='{System.Net.WebUtility.HtmlEncode(u)}'>
<p><label><input type='checkbox' name='deleteConfig'> Удалить конфиг + peer на сервере</label></p>
<p><button type='submit'>Удалить</button> <a href='/admin'>Отмена</a></p>
</form></body></html>";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/admin/delete", async (HttpRequest req, WireGuardService wg) =>
{
    var form = await req.ReadFormAsync();
    var u = form["u"].ToString();
    var deleteConfig = form.ContainsKey("deleteConfig");
    await wg.DeleteUser(u, deleteConfig);
    return Results.Redirect("/admin");
});

app.Run();
