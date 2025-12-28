using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using ConnectionRevitCloud.Client.ViewModels;

namespace ConnectionRevitCloud.Client.Tray;

public class TrayIcon
{
    private readonly Window _window;
    private readonly MainViewModel _vm;
    private TaskbarIcon? _icon;

    public TrayIcon(Window window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
    }

    public void Init()
    {
        _icon = new TaskbarIcon
        {
            Icon = new System.Drawing.Icon("Assets/icon.ico"),
            ToolTipText = "ConnectionRevitCloud"
        };

        var menu = new System.Windows.Controls.ContextMenu();

        var miShow = new System.Windows.Controls.MenuItem { Header = "Показать" };
        miShow.Click += (_, _) => { _window.Show(); _window.Activate(); };

        var miConnect = new System.Windows.Controls.MenuItem { Header = "Подключить" };
        miConnect.Click += async (_, _) => { _window.Show(); _window.Activate(); };

        var miDisconnect = new System.Windows.Controls.MenuItem { Header = "Отключить" };
        miDisconnect.Click += async (_, _) => await _vm.DisconnectAsync();

        var miExit = new System.Windows.Controls.MenuItem { Header = "Выход" };
        miExit.Click += (_, _) =>
        {
            _icon!.Dispose();
            Application.Current.Shutdown();
        };

        menu.Items.Add(miShow);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(miConnect);
        menu.Items.Add(miDisconnect);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(miExit);

        _icon.ContextMenu = menu;

        _icon.TrayLeftMouseDown += (_, _) =>
        {
            _window.Show();
            _window.Activate();
        };
    }
}
