using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IpodSync.Core.Devices;
using IpodSync.Desktop.Views;

namespace IpodSync.Desktop.Tests;

/// <summary>
/// El browser de biblioteca es de solo lectura sobre lo que YA está en el
/// iPod: un panel más de MainWindow, no una ventana nueva.
/// LibraryBrowserTests (Core) ya cubre el agrupamiento y la detección de
/// carátulas; acá solo importa que el panel cambie sin crear nada, y que
/// alternar Álbumes/Canciones y Lista/Grilla no tire excepciones.
/// </summary>
public class LibraryPanelTests : IDisposable
{
    private class FakeDetector : IDeviceDetector
    {
        public IpodDevice? Device;
        public IpodDevice? Detect() => Device;
    }

    private readonly string _mount;

    public LibraryPanelTests()
    {
        _mount = Path.Combine(Path.GetTempPath(), "ipodsync-librarypanel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_mount);
    }

    public void Dispose()
    {
        App.Devices.Stop();
        try { Directory.Delete(_mount, recursive: true); } catch { }
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Mismo patrón que AppLifecycleTests: un detector falso, sin
    /// tocar hardware real.</summary>
    private void ConnectFakeDevice()
    {
        var detector = new FakeDetector { Device = new IpodDevice(_mount, "IPOD", 1L << 30, 1L << 31) };
        App.ResetDevicesForTests(detector);
        App.Devices.PollNow();
    }

    private static async Task PumpUntil(Func<bool> done, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Sin_dispositivo_el_boton_de_biblioteca_esta_deshabilitado()
    {
        // App.Devices es un singleton compartido por todo el proceso de
        // tests: sin resetearlo acá, un test ANTERIOR que dejó un
        // dispositivo falso conectado (y solo hizo Stop(), que no limpia
        // Current) filtra ese estado a este test. ResetDevicesForTests
        // arranca un DeviceWatcher nuevo, con Current en null de nuevo.
        App.ResetDevicesForTests(new FakeDetector());
        var window = new MainWindow();

        Assert.False(window.GetControl<Button>("LibraryButton").IsEnabled);
    }

    [AvaloniaFact]
    public void Abrir_biblioteca_cambia_el_panel_sin_crear_ventana_nueva()
    {
        ConnectFakeDevice();
        var window = new MainWindow();

        Click(window.GetControl<Button>("LibraryButton"));

        Assert.False(window.GetControl<Control>("MainPanel").IsVisible);
        Assert.True(window.GetControl<Control>("LibraryPanel").IsVisible);
    }

    [AvaloniaFact]
    public void Cerrar_biblioteca_vuelve_al_panel_principal()
    {
        ConnectFakeDevice();
        var window = new MainWindow();
        Click(window.GetControl<Button>("LibraryButton"));

        Click(window.GetControl<Button>("LibraryCloseButton"));

        Assert.True(window.GetControl<Control>("MainPanel").IsVisible);
        Assert.False(window.GetControl<Control>("LibraryPanel").IsVisible);
    }

    [AvaloniaFact]
    public async Task Sin_musica_en_el_ipod_muestra_el_mensaje_vacio()
    {
        ConnectFakeDevice();   // el mount existe pero no tiene Music/ todavía
        var window = new MainWindow();

        Click(window.GetControl<Button>("LibraryButton"));
        var empty = window.GetControl<TextBlock>("LibraryEmptyText");
        await PumpUntil(() => empty.IsVisible);

        Assert.True(empty.IsVisible);
    }

    [AvaloniaFact]
    public async Task Con_albumes_reales_arma_la_grilla_y_alterna_de_vista_sin_tirar_excepcion()
    {
        Directory.CreateDirectory(Path.Combine(_mount, "Music", "Album Uno"));
        File.WriteAllText(Path.Combine(_mount, "Music", "Album Uno", "01.flac"), "x");
        File.WriteAllText(Path.Combine(_mount, "Music", "Album Uno", "02.flac"), "x");
        ConnectFakeDevice();

        var window = new MainWindow();
        var host = window.GetControl<ContentControl>("LibraryItemsHost");

        Click(window.GetControl<Button>("LibraryButton"));
        await PumpUntil(() => host.Content is Panel p && p.Children.Count > 0);

        Assert.IsType<WrapPanel>(host.Content);   // grilla es el default
        Assert.Single(((Panel)host.Content!).Children);   // un álbum

        Click(window.GetControl<Button>("LibraryListLayoutButton"));
        Assert.IsType<StackPanel>(host.Content);

        Click(window.GetControl<Button>("LibrarySongsModeButton"));
        Assert.Equal(2, ((Panel)host.Content!).Children.Count);   // dos canciones

        Click(window.GetControl<Button>("LibraryGridLayoutButton"));
        Assert.IsType<WrapPanel>(host.Content);
        Assert.Equal(2, ((Panel)host.Content!).Children.Count);
    }

    [AvaloniaFact]
    public async Task Clickear_un_album_muestra_sus_canciones_y_el_boton_volver()
    {
        Directory.CreateDirectory(Path.Combine(_mount, "Music", "Album Uno"));
        File.WriteAllText(Path.Combine(_mount, "Music", "Album Uno", "01.flac"), "x");
        ConnectFakeDevice();

        var window = new MainWindow();
        var host = window.GetControl<ContentControl>("LibraryItemsHost");
        var back = window.GetControl<Button>("LibraryBackButton");

        Click(window.GetControl<Button>("LibraryButton"));
        await PumpUntil(() => host.Content is Panel p && p.Children.Count > 0);
        Assert.False(back.IsVisible);

        // El único hijo es el Button que envuelve el tile del álbum.
        Click(Assert.IsType<Button>(((Panel)host.Content!).Children[0]));

        Assert.True(back.IsVisible);
        Assert.Single(((Panel)host.Content!).Children);   // la única canción del álbum

        Click(back);
        Assert.False(back.IsVisible);
    }
}
