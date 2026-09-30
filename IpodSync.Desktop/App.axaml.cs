using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using IpodSync.Core.Devices;
using IpodSync.Core.Services;
using IpodSync.Desktop.Localization;
using IpodSync.Desktop.Views;

namespace IpodSync.Desktop;

public partial class App : Application
{
    public static SettingsService Settings { get; } = new();

    /// <summary>
    /// Un solo vigilante para toda la app. Vive acá y no en la ventana porque
    /// tiene que seguir funcionando con la ventana cerrada en la bandeja:
    /// ese es justo el caso de uso de enchufar el iPod y que se sincronice.
    /// </summary>
    public static DeviceWatcher Devices { get; private set; } = new();

    /// <summary>
    /// Solo para IpodSync.Desktop.Tests: reemplaza el vigilante por uno con
    /// un detector falso, para poder probar el guardia de instancia única de
    /// TryOpenSync con un "iPod" controlado en vez de depender de que la
    /// máquina de test tenga uno real conectado.
    /// </summary>
    internal static void ResetDevicesForTests(IDeviceDetector detector)
    {
        Devices.Dispose();
        Devices = new DeviceWatcher(detector, TimeSpan.FromMilliseconds(20));
    }

    private MainWindow? _main;

    /// <summary>Solo para IpodSync.Desktop.Tests: acceder a la MainWindow
    /// compartida sin reflection, para simular clicks en sus paneles.</summary>
    internal MainWindow? MainWindowForTests => _main;

    /// <summary>Resultado de pedir el panel de sincronización.</summary>
    public enum SyncOpenResult { Opened, AlreadyOpen, NoDevice }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = Settings.Load();

            // Antes de construir cualquier ventana: si no, la primera se
            // arma con el default de L (español) y queda mal traducida
            // hasta el próximo cambio de idioma.
            L.Initialize(settings.Language);
            EnsureTraySetup();

            // Cerrar la ventana la manda a la bandeja, no cierra la app. Sin
            // esto el icono de bandeja desaparecería con la ventana.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!settings.IsConfigured)
            {
                // Primera ejecución: no tiene sentido vigilar nada todavía.
                // EnterConfiguredState() arranca el vigilante recién cuando
                // ConnectionWindow guarda una configuración válida.
                desktop.MainWindow = new ConnectionWindow();
                desktop.MainWindow.Show();
            }
            else
            {
                EnterConfiguredState();

                // --minimized lo pone el arranque automático: iniciar sesión
                // no debería tapar la pantalla con una ventana.
                if (!desktop.Args?.Contains("--minimized") ?? true)
                    ShowMain();
            }

            desktop.Exit += (_, _) => Devices.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // ------------------------------------------------------------ ciclo de vida

    private bool _watcherStarted;

    /// <summary>
    /// Arranca el vigilante del iPod. Idempotente: la llama tanto el arranque
    /// normal como ConnectionWindow al terminar la primera configuración, y
    /// en ese segundo caso antes no se llamaba a nada de esto — el botón
    /// "Sincronizar" quedaba deshabilitado para siempre después de configurar
    /// la app por primera vez, porque nadie había arrancado el sondeo.
    /// </summary>
    public void EnterConfiguredState()
    {
        if (_watcherStarted) return;
        _watcherStarted = true;

        Devices.Connected += OnDeviceConnected;
        Devices.Start();
    }

    // ------------------------------------------------------------ ventanas

    /// <summary>Crea MainWindow si todavía no existe. Idempotente: tanto
    /// ShowMain como TryOpenSync la necesitan, y solo tiene que armarse (y
    /// suscribirse a sus eventos) una vez.</summary>
    private void EnsureMain()
    {
        if (_main is not null) return;

        _main = new MainWindow();
        _main.Closed += (_, _) => _main = null;

        // Se pausó el sondeo al arrancar la sync (más abajo); esto es lo que
        // lo reanuda cuando el panel vuelve al principal, sea porque terminó,
        // se canceló, o el usuario lo cerró.
        _main.SyncPanelClosed += () =>
        {
            Devices.Paused = false;
            Devices.PollNow();
        };
    }

    /// <summary>Interna y no privada: ConnectionWindow la necesita para
    /// mostrar la MISMA MainWindow que el resto de la app rastrea después del
    /// setup inicial (ver el comentario en ConnectionWindow.OnSave).</summary>
    internal void ShowMain()
    {
        EnsureMain();
        _main!.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    /// <summary>
    /// Único punto de entrada para arrancar una sincronización, con guardia
    /// de instancia única. Sin esto, apretar "Sincronizar" en la ventana
    /// principal y "Sincronizar ahora" en la bandeja al mismo tiempo abriría
    /// DOS transferencias sobre el mismo iPod a la vez — la misma familia de
    /// condición de carrera que ya lo corrompió antes.
    ///
    /// Un solo shell para toda la app: no crea una ventana nueva, cambia el
    /// panel visible de la MISMA MainWindow (creándola si hace falta) y la
    /// trae al frente.
    /// </summary>
    public SyncOpenResult TryOpenSync(bool autoStart)
    {
        if (_main is { IsSyncing: true })
        {
            _main.Show();
            _main.WindowState = WindowState.Normal;
            _main.Activate();
            return SyncOpenResult.AlreadyOpen;
        }

        if (Devices.Current is not { } device) return SyncOpenResult.NoDevice;

        // Mientras se transfiere, el sondeo del disco compite con la
        // escritura y una detección espuria arrancaría una segunda sync.
        Devices.Paused = true;

        EnsureMain();
        _main!.ShowSyncPanel(Settings.Load(), device, autoStart);
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();

        return SyncOpenResult.Opened;
    }

    private void OnDeviceConnected(IpodDevice device)
    {
        var settings = Settings.Load();
        if (!settings.AutoSyncOnConnect) return;

        // El evento llega desde un hilo del pool.
        Dispatcher.UIThread.Post(() => TryOpenSync(autoStart: true));
    }

    // -------------------------------------------------------------- bandeja

    /// <summary>La bandeja vive mientras vive el proceso: no hace falta
    /// desuscribirse de L.Changed, no hay una instancia "vieja" que limpiar.</summary>
    private bool _traySetup;

    /// <summary>
    /// Idempotente, como EnterConfiguredState: el ciclo de vida completo de
    /// OnFrameworkInitializationCompleted no corre bajo Avalonia.Headless en
    /// los tests (ApplicationLifetime no es IClassicDesktopStyleApplication-
    /// Lifetime ahí), así que sin este método separado no habría forma de
    /// probar que la bandeja de verdad reacciona a un cambio de idioma.
    /// </summary>
    internal void EnsureTraySetup()
    {
        if (_traySetup) return;
        _traySetup = true;

        ApplyTrayLanguage();
        L.Changed += ApplyTrayLanguage;
    }

    private void ApplyTrayLanguage()
    {
        // Ningún x:Name bajo TrayIcon.Icons genera campo de code-behind (ni
        // siquiera el del propio TrayIcon): ese árbol no pasa por el mismo
        // name-scope que el contenido de una ventana. Se llega por la API
        // estática de Avalonia en vez de por nombre.
        var icons = Avalonia.Controls.TrayIcon.GetIcons(this);
        if (icons is not { Count: > 0 } || icons[0].Menu is not { } menu) return;

        // Índices fijos: 0=Abrir, 1=Sincronizar, 2=separador, 3=Salir.
        ((Avalonia.Controls.NativeMenuItem)menu.Items[0]).Header = L.T("tray.open");
        ((Avalonia.Controls.NativeMenuItem)menu.Items[1]).Header = L.T("tray.sync");
        ((Avalonia.Controls.NativeMenuItem)menu.Items[3]).Header = L.T("tray.exit");
    }

    private void OnTrayClicked(object? sender, EventArgs e) => ShowConfiguredOrSetup();

    private void OnTrayOpen(object? sender, EventArgs e) => ShowConfiguredOrSetup();

    private void OnTraySync(object? sender, EventArgs e)
    {
        if (!Settings.Load().IsConfigured) { ShowConfiguredOrSetup(); return; }

        // TryOpenSync ya se encarga de mostrar y activar MainWindow en el
        // caso Opened/AlreadyOpen; acá solo hace falta cubrir NoDevice.
        if (TryOpenSync(autoStart: false) == SyncOpenResult.NoDevice) ShowMain();
    }

    /// <summary>
    /// Punto de entrada único desde la bandeja: antes de que exista una
    /// configuración válida no tiene sentido mostrar la ventana principal
    /// (se quedaría con host vacío tratando de conectarse a ningún lado).
    /// </summary>
    private void ShowConfiguredOrSetup()
    {
        if (Settings.Load().IsConfigured) ShowMain();
        else
        {
            var setup = new ConnectionWindow();
            setup.Show();
            setup.Activate();
        }
    }

    private void OnTrayExit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Devices.Dispose();
            desktop.Shutdown();
        }
    }
}
