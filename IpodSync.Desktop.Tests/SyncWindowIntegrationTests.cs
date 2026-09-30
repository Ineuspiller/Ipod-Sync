using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IpodSync.Core.Devices;
using IpodSync.Core.Services;
using IpodSync.Desktop.Localization;
using IpodSync.Desktop.Views;
using Xunit.Abstractions;

namespace IpodSync.Desktop.Tests;

/// <summary>
/// Sincronización completa manejada por el panel de sync de MainWindow,
/// contra un servidor de verdad. Es **opt-in**: sin las dos variables de
/// entorno no hace nada, para que la suite normal no dependa de tener SSH ni
/// una biblioteca a mano.
///
///   IPODSYNC_TEST_CONFIG=/ruta/settings.json \
///   IPODSYNC_TEST_TARGET=/tmp/ipod-de-prueba \
///   dotnet test IpodSync.Desktop.Tests
/// </summary>
public class SyncWindowIntegrationTests
{
    private static string? ConfigPath => Environment.GetEnvironmentVariable("IPODSYNC_TEST_CONFIG");
    private static string? TargetPath => Environment.GetEnvironmentVariable("IPODSYNC_TEST_TARGET");

    private readonly ITestOutputHelper _out;
    public SyncWindowIntegrationTests(ITestOutputHelper output) => _out = output;

    [AvaloniaFact]
    public async Task La_ventana_sincroniza_de_punta_a_punta()
    {
        if (ConfigPath is null || TargetPath is null)
        {
            _out.WriteLine("Omitido: faltan IPODSYNC_TEST_CONFIG e IPODSYNC_TEST_TARGET.");
            return;
        }

        Directory.CreateDirectory(TargetPath);

        var settings = new SettingsService(ConfigPath).Load();
        var device = new IpodDevice(TargetPath, "IPOD", 1L << 30, 1L << 31);

        // Este test valida la MECÁNICA de la sync, no la traducción (eso ya
        // lo cubre WindowLocalizationTests): se fuerza español sin importar
        // lo que diga el archivo de settings de quien lo corra, para que las
        // aserciones de abajo no dependan de un idioma ajeno al test.
        var previousLanguage = L.Current;
        L.Set(Core.Models.AppLanguage.Spanish);

        try
        {
            var window = new MainWindow();
            window.Show();
            window.ShowSyncPanel(settings, device, autoStart: false);

            var title = window.GetControl<TextBlock>("PhaseTitle");
            var primary = window.GetControl<Button>("SyncPrimaryButton");
            var log = window.GetControl<SelectableTextBlock>("Log");

            // Fase 1: análisis. El trabajo va en Task.Run, así que hay que
            // dejar correr el dispatcher para que los reportes lleguen.
            await PumpUntil(() => primary.IsEnabled, seconds: 60);

            _out.WriteLine($"análisis: {title.Text} | {log.Text}");
            Assert.True(primary.IsEnabled, "El análisis nunca terminó.");

            if (title.Text == "Todo al día") return;   // nada que sincronizar
            Assert.Equal("Listo para sincronizar", title.Text);

            // Fase 2: confirmar y transferir.
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpUntil(() => title.Text is "Listo" or "Terminó con errores", seconds: 120);

            _out.WriteLine($"final: {title.Text}\n{log.Text}");
            Assert.Equal("Listo", title.Text);

            // Lo que quedó en el "iPod" es lo único que importa.
            Assert.True(File.Exists(Path.Combine(TargetPath, ".ipod-sync-state.json")),
                        "No se escribió el estado en la raíz.");
            Assert.Empty(Directory.EnumerateFiles(TargetPath, "*.m3u", SearchOption.AllDirectories));
        }
        finally
        {
            L.Set(previousLanguage);
        }
    }

    private static async Task PumpUntil(Func<bool> done, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
