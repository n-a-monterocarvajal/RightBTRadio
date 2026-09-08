using Microsoft.UI.Xaml;

namespace RightBTRadio.WinUI;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new SettingsWindow();
        window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Write($"Excepción no controlada en la ventana de ajustes: {e.Message}");
    }
}
