using System.Windows;
using ConnectionRevitCloud.Client.Tray;
using ConnectionRevitCloud.Client.ViewModels;

namespace ConnectionRevitCloud.Client;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TrayIcon _tray;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _tray = new TrayIcon(this, _vm);
        _tray.Init();

        Closing += (s, e) =>
        {
            // закрытие окна = в трей
            e.Cancel = true;
            Hide();
        };
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ConnectAsync(Pwd.Password);
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        await _vm.DisconnectAsync();
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}
