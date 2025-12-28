using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ConnectionRevitCloud.Server.Data;
using ConnectionRevitCloud.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ConnectionRevitCloud.Server.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public AuthService(AppDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    public async Task<string?> Login(string username, string password)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Username == username);
        if (u is null) return null;
        if (!u.IsEnabled) return null;
        if (!BCrypt.Net.BCrypt.Verify(password, u.PasswordHash)) return null;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Security:JwtKey"] ?? "CHANGE_ME"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, u.Username),
            new Claim("wgip", u.WgIp)
        };

        var token = new JwtSecurityToken(
            issuer: _cfg["Security:JwtIssuer"],
            audience: _cfg["Security:JwtAudience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Task<User?> GetUser(string username)
        => _db.Users.FirstOrDefaultAsync(x => x.Username == username);

    public async Task CreateUser(string username, string password, string wgip, string configPath)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var u = new User
        {
            Username = username,
            PasswordHash = hash,
            WgIp = wgip,
            ConfigPath = configPath,
            IsEnabled = true
        };
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
    }

    public async Task ToggleUser(string username)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Username == username);
        if (u is null) return;
        u.IsEnabled = !u.IsEnabled;
        await _db.SaveChangesAsync();
    }
}
