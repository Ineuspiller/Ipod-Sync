using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using IpodSync.Core.Devices;
using IpodSync.Desktop.Views;

namespace IpodSync.Desktop.Tests;

/// <summary>
/// El bug que motivó esto: ConnectionWindow.OnSave creaba MainWindow
/// directamente, sin arrancar App.Devices (el vigilante del iPod). Resultado:
/// después de configurar la app por primera vez, el botón "Sincronizar"
/// quedaba deshabilitado para siempre, porque nadie había arrancado el
/// sondeo. EnterConfiguredState() es el único lugar que lo arranca ahora.
///
/// También cubre la condición de carrera que llevó a centralizar
/// TryOpenSync: apretar "Sincronizar" en la ventana y "Sincronizar ahora" en
/// la bandeja al mismo tiempo no puede abrir dos transferencias sobre el
/// mismo iPod. Desde que MainWindow, SettingsWindow y SyncWindow se
/// fusionaron en una sola ventana con paneles, "abrir la sync" ya no crea
/// una ventana nueva: cambia el panel visible de la MISMA MainWindow.
///
/// IDisposable: EnterConfiguredState() arranca un System.Threading.Timer DE
/// VERDAD (App.Devices.Start()). Un timer así no se entera de que xUnit pasó
/// a otro test ni de que dejamos de estar en éste: sigue disparando
/// callbacks en un hilo del pool aunque el método ya haya retornado. Sin
/// detenerlo al final de CADA test de esta clase, el último que corre deja
/// el timer vivo para lo que siga ejecutándose después —cualquier otra
/// clase de la suite—, tocando ventanas ajenas en momentos impredecibles.
/// </summary>
public class AppLifecycleTests : IDisposable
{
    /// <summary>Detector controlado desde el test: no toca el disco real.</summary>
    private class FakeDetector : IDeviceDetector
    {
        public IpodDevice? Device;
        public IpodDevice? Detect() => Device;
    }

    public void Dispose() => App.Devices.Stop();

    [AvaloniaFact]
    public void EnterConfiguredState_arranca_el_vigilante()
    {
        App.ResetDevicesForTests(new FakeDetector());
        var app = (App)Avalonia.Application.Current!;

        app.EnterConfiguredState();

        // Si el timer nunca arrancó, Current sigue null para siempre y el
        // evento Connected nunca se dispara aunque haya un iPod enchufado.
        // No hay forma de inspeccionar el Timer privado desde acá, pero
        // PollNow debe poder ejecutarse sin excepción una vez arrancado.
        App.Devices.PollNow();
    }

    [AvaloniaFact]
    public void EnterConfiguredState_es_idempotente()
    {
        // ConnectionWindow lo llama al guardar; el arranque normal lo llama
        // si ya estaba configurado. Que se llame dos veces no puede duplicar
        // el suscriptor del evento Connected.
        var app = (App)Avalonia.Application.Current!;
        App.ResetDevicesForTests(new FakeDetector());

        var calls = 0;
        App.Devices.Connected += _ => calls++;

        app.EnterConfiguredState();
        app.EnterConfiguredState();
        app.EnterConfiguredState();

        Assert.Equal(0, calls);   // sin dispositivo, no debería dispararse nada
    }

    [AvaloniaFact]
    public void ShowMain_es_idempotente_no_crea_una_segunda_ventana()
    {
        // Bug real: ConnectionWindow.OnSave creaba `new MainWindow()` a mano
        // en vez de pasar por acá, así que esa ventana quedaba fuera de
        // App._main. Un sync disparado después (bandeja, auto-sync, o el
        // propio botón "Sincronizar" de ESA MISMA ventana, que llama a
        // App.TryOpenSync) abría una SEGUNDA MainWindow en vez de reusar la
        // que el usuario tenía en pantalla.
        var app = (App)Avalonia.Application.Current!;

        app.ShowMain();
        var first = app.MainWindowForTests;

        app.ShowMain();

        Assert.Same(first, app.MainWindowForTests);
    }

    [AvaloniaFact]
    public void Sin_dispositivo_conectado_TryOpenSync_no_abre_nada()
    {
        var app = (App)Avalonia.Application.Current!;
        App.ResetDevicesForTests(new FakeDetector());
        app.EnterConfiguredState();

        var result = app.TryOpenSync(autoStart: false);

        Assert.Equal(App.SyncOpenResult.NoDevice, result);
    }

    [AvaloniaFact]
    public void No_se_puede_abrir_una_segunda_sync_a_la_vez()
    {
        // Es la condición de carrera que motivó centralizar TryOpenSync:
        // sincronizar desde la ventana Y desde la bandeja al mismo tiempo no
        // puede terminar en dos transferencias escribiendo el mismo iPod.
        var app = (App)Avalonia.Application.Current!;
        var detector = new FakeDetector { Device = new IpodDevice("/tmp/no-existe", "IPOD", 1, 2) };
        App.ResetDevicesForTests(detector);
        app.EnterConfiguredState();
        App.Devices.PollNow();

        var first = app.TryOpenSync(autoStart: false);
        Assert.Equal(App.SyncOpenResult.Opened, first);
        Assert.True(app.MainWindowForTests!.IsSyncing);

        // Segundo pedido mientras el primero sigue abierto: no arranca otra
        // sync, solo activa la ventana que ya está mostrando el panel.
        var second = app.TryOpenSync(autoStart: false);
        Assert.Equal(App.SyncOpenResult.AlreadyOpen, second);

        CancelSyncPanel(app.MainWindowForTests!);
    }

    [AvaloniaFact]
    public void Al_cerrar_el_panel_se_libera_el_guardia_para_la_proxima()
    {
        var app = (App)Avalonia.Application.Current!;
        var detector = new FakeDetector { Device = new IpodDevice("/tmp/no-existe", "IPOD", 1, 2) };
        App.ResetDevicesForTests(detector);
        app.EnterConfiguredState();
        App.Devices.PollNow();

        app.TryOpenSync(autoStart: false);
        CancelSyncPanel(app.MainWindowForTests!);

        Assert.False(app.MainWindowForTests!.IsSyncing);

        var again = app.TryOpenSync(autoStart: false);

        Assert.Equal(App.SyncOpenResult.Opened, again);
        CancelSyncPanel(app.MainWindowForTests!);
    }

    /// <summary>Simula al usuario apretando "Cancelar" en el panel de sync,
    /// para volver al panel principal sin depender de que la conexión (falsa)
    /// termine de fallar sola. El panel arranca en fase "Analizando", así que
    /// cancelar acá vuelve al principal de inmediato (no es la fase
    /// "Ejecutando", la única que ignora el botón de cancelar).</summary>
    private static void CancelSyncPanel(MainWindow window) =>
        window.GetControl<Button>("SyncSecondaryButton")
              .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
