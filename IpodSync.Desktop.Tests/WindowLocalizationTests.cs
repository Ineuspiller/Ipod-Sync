using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IpodSync.Core.Models;
using IpodSync.Desktop.Localization;
using IpodSync.Desktop.Views;

namespace IpodSync.Desktop.Tests;

/// <summary>
/// Prueba el cableado real entre L y cada ventana: que cambiar el idioma
/// efectivamente cambie lo que se ve, no solo que la tabla de strings esté
/// completa. Es lo que detecta un control al que me olvidé de conectar.
/// </summary>
public class WindowLocalizationTests
{
    private static void WithLanguage(AppLanguage lang, Action body)
    {
        var previous = L.Current;
        try { L.Set(lang); body(); }
        finally { L.Set(previous); }
    }

    [AvaloniaFact]
    public void ConnectionWindow_cambia_de_idioma_en_vivo()
    {
        WithLanguage(AppLanguage.Spanish, () =>
        {
            var window = new ConnectionWindow();
            var heading = window.GetControl<TextBlock>("HeadingText");
            var save = window.GetControl<Button>("SaveButton");

            Assert.Equal("Conectar al servidor", heading.Text);

            L.Set(AppLanguage.English);

            Assert.Equal("Connect to server", heading.Text);
            Assert.Equal("Save and continue", save.Content);
        });
    }

    [AvaloniaFact]
    public void ConnectionWindow_arranca_en_el_idioma_guardado()
    {
        WithLanguage(AppLanguage.English, () =>
        {
            var window = new ConnectionWindow();

            Assert.Equal("Connect to server", window.GetControl<TextBlock>("HeadingText").Text);
            Assert.Equal(1, window.GetControl<ComboBox>("LanguageBox").SelectedIndex);
        });
    }

    /// <summary>Cuenta los suscriptores del evento estático por reflection:
    /// es la única forma de verificar una desuscripción desde afuera.</summary>
    private static int CountLanguageChangedSubscribers()
    {
        var field = typeof(L).GetField("Changed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return field?.GetValue(null) is MulticastDelegate d ? d.GetInvocationList().Length : 0;
    }

    [AvaloniaFact]
    public void ConnectionWindow_se_desuscribe_al_cerrarse()
    {
        // Si no se desuscribe, cada ConnectionWindow que se abrió y cerró
        // ("Configuración" desde MainWindow puede crear varias en la vida de
        // la app) queda escuchando L.Changed para siempre: una fuga chica
        // pero indefinida a lo largo de una sesión larga.
        var before = CountLanguageChangedSubscribers();

        var window = new ConnectionWindow();
        Assert.Equal(before + 1, CountLanguageChangedSubscribers());

        window.Close();
        Assert.Equal(before, CountLanguageChangedSubscribers());
    }

    [AvaloniaFact]
    public void MainWindow_se_desuscribe_al_cerrarse()
    {
        var before = CountLanguageChangedSubscribers();

        var window = new MainWindow();
        Assert.Equal(before + 1, CountLanguageChangedSubscribers());

        window.Close();
        Assert.Equal(before, CountLanguageChangedSubscribers());
    }

    [AvaloniaFact]
    public void MainWindow_cambia_de_idioma_en_vivo()
    {
        WithLanguage(AppLanguage.Spanish, () =>
        {
            var window = new MainWindow();
            var sync = window.GetControl<Button>("SyncButton");
            var settings = window.GetControl<Button>("SettingsButton");

            Assert.Equal("Sincronizar", sync.Content);
            Assert.Equal("Configuración", settings.Content);

            L.Set(AppLanguage.English);

            Assert.Equal("Sync", sync.Content);
            Assert.Equal("Settings", settings.Content);
        });
    }

    [AvaloniaFact]
    public void MainWindow_sin_iPod_muestra_el_estado_traducido()
    {
        // App.Devices es estático y otras clases de test (AppLifecycleTests)
        // lo dejan con un dispositivo falso conectado; sin resetearlo acá
        // este test depende del orden de ejecución de TODA la suite.
        App.ResetDevicesForTests(new NoDeviceDetector());

        WithLanguage(AppLanguage.English, () =>
        {
            var window = new MainWindow();
            Assert.Equal("Not detected", window.GetControl<TextBlock>("IpodStatus").Text);
        });
    }

    private class NoDeviceDetector : Core.Devices.IDeviceDetector
    {
        public Core.Devices.IpodDevice? Detect() => null;
    }

    [AvaloniaFact]
    public void SettingsPanel_el_texto_del_borrado_cambia_segun_el_estado()
    {
        WithLanguage(AppLanguage.English, () =>
        {
            var window = new MainWindow();
            var check = window.GetControl<ToggleSwitch>("DeleteCheck");
            var detail = window.GetControl<TextBlock>("DeleteDetail");

            check.IsChecked = true;
            Assert.Contains("deleted from the iPod", detail.Text);

            check.IsChecked = false;
            Assert.Contains("accumulate", detail.Text);
        });
    }

    [AvaloniaFact]
    public void SyncPanel_arranca_con_el_boton_cancelar_traducido()
    {
        WithLanguage(AppLanguage.English, () =>
        {
            var settings = new AppSettings { Host = "h", Username = "u" };
            var device = new Core.Devices.IpodDevice("/tmp/no-existe", "IPOD", 1, 2);

            var window = new MainWindow();
            window.ShowSyncPanel(settings, device, autoStart: false);

            Assert.Equal("Cancel", window.GetControl<Button>("SyncSecondaryButton").Content);
            Assert.Equal("Analyzing...", window.GetControl<TextBlock>("PhaseTitle").Text);
        });
    }

    [AvaloniaFact]
    public void La_bandeja_cambia_de_idioma_en_vivo()
    {
        WithLanguage(AppLanguage.Spanish, () =>
        {
            // El ciclo de vida completo no corre bajo el harness headless:
            // sin esto la bandeja nunca se suscribió a L.Changed.
            ((App)Avalonia.Application.Current!).EnsureTraySetup();

            var icons = Avalonia.Controls.TrayIcon.GetIcons(Avalonia.Application.Current!);
            Assert.NotNull(icons);
            Assert.True(icons!.Count > 0);

            var menu = icons[0].Menu;
            Assert.NotNull(menu);

            Assert.Equal("Abrir", ((Avalonia.Controls.NativeMenuItem)menu!.Items[0]).Header);

            L.Set(AppLanguage.English);

            Assert.Equal("Open", ((Avalonia.Controls.NativeMenuItem)menu.Items[0]).Header);
            Assert.Equal("Sync now", ((Avalonia.Controls.NativeMenuItem)menu.Items[1]).Header);
            Assert.Equal("Exit", ((Avalonia.Controls.NativeMenuItem)menu.Items[3]).Header);
        });
    }
}
