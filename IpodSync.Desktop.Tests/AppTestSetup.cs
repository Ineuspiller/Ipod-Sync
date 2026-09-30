using Avalonia;
using Avalonia.Headless;
using IpodSync.Desktop;

[assembly: AvaloniaTestApplication(typeof(IpodSync.Desktop.Tests.AppTestSetup))]

namespace IpodSync.Desktop.Tests;

/// <summary>
/// Arranque de Avalonia sin ventana real. Permite ejecutar la UI en entornos
/// headless (CI, un servidor sin pantalla). Sin esto la UI solo se podría
/// compilar, nunca correr ahí.
/// </summary>
public static class AppTestSetup
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
