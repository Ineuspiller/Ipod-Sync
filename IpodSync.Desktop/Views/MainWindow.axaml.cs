using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using IpodSync.Core.Devices;
using IpodSync.Core.Models;
using IpodSync.Core.Services;
using IpodSync.Desktop.Localization;

namespace IpodSync.Desktop.Views;

/// <summary>
/// Único shell de la app: estado, ajustes y sincronización viven en la MISMA
/// ventana, cambiando de panel en vez de abrir una ventana nueva por cada
/// paso. Antes había MainWindow + SettingsWindow + SyncWindow como tres
/// ventanas separadas; un usuario real las vio abiertas y superpuestas al
/// mismo tiempo y no le encontró sentido. Solo <see cref="ConnectionWindow"/>
/// sigue siendo una ventana aparte: es la pantalla de setup inicial (o de
/// editar la conexión), no un paso del uso normal del día a día.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly IBrush Ok   = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush Bad  = new SolidColorBrush(Color.Parse("#FF6B6B"));
    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#999999"));

    private readonly AppSettings _settings;
    private readonly IStartupManager _startup;

    // Se guardan para poder re-dibujar el estado del servidor en el idioma
    // nuevo si el usuario cambia de idioma después de conocer el resultado;
    // si no, un cambio de idioma solo actualizaría las etiquetas fijas y
    // dejaría ese texto congelado en el idioma viejo.
    private ConnectionResult? _lastConnectionResult;
    private string? _lastConnectionException;

    // Estado del panel de biblioteca: qué se escaneó, qué se está mostrando
    // (álbumes o canciones, lista o grilla) y un filtro opcional cuando se
    // entra a un álbum puntual desde la grilla/lista de álbumes.
    private List<AlbumEntry> _libraryAlbums = new();
    private string? _libraryMusicRoot;
    private string? _libraryMountRoot;
    private bool _libraryGridMode = true;
    private bool _librarySongsMode;
    private AlbumEntry? _libraryFilter;

    // Decodificadas una sola vez por ruta y reusadas entre Álbumes/Canciones
    // y Lista/Grilla: cambiar de vista no debería releer el disco del iPod.
    private readonly Dictionary<string, Bitmap?> _coverCache = new();

    public MainWindow()
    {
        InitializeComponent();

#if DEBUG
        // F12 abre el inspector de Avalonia: árbol visual, cada propiedad de
        // cada control, en vivo. El paquete ya estaba referenciado (solo en
        // Debug, ver el .csproj) pero nadie lo conectaba. Es la herramienta
        // más directa para tocar la UI sin recompilar en cada cambio.
        Avalonia.DevToolsExtensions.AttachDevTools(this);
#endif

        _settings = App.Settings.Load();
        _startup = StartupManagerFactory.Create();

        ServerLabel.Text = $"{_settings.Username}@{_settings.Host}:{_settings.Port}";

        ApplyLanguage();
        L.Changed += ApplyLanguage;

        // El sondeo del dispositivo lo hace App: uno solo para toda la app,
        // que sigue corriendo con la ventana cerrada en la bandeja.
        App.Devices.Connected += OnDeviceChanged;
        App.Devices.Disconnected += OnDeviceGone;

        ShowDevice(App.Devices.Current);
        _ = VerifyServerAsync();

        InitSyncPanelChrome();
    }

    private void ApplyLanguage()
    {
        ServerSectionText.Text  = L.T("common.section.server");
        IpodSectionText.Text    = L.T("common.section.ipod");
        DiskModeHintText.Text   = L.T("main.diskmode.hint");
        SettingsButton.Content  = L.T("main.settings");
        SyncButton.Content      = L.T("main.sync");
        if (RescanButton.IsEnabled) RescanButton.Content = L.T("main.rescan");
        LibraryButtonText.Text = L.T("main.library");
        LibraryButtonHint.Text = L.T("main.library.hint");

        SettingsHeadingText.Text     = L.T("main.settings");
        AutoSyncCheck.Content        = L.T("main.autosync");
        StartupCheck.Content         = L.T("main.startup");
        SkipConfirmCheck.Content     = L.T("settings.skipConfirm");
        SkipConfirmHint.Text         = L.T("settings.skipConfirm.hint");
        DeleteCheck.Content          = L.T("main.delete");
        LanguageLabel.Text           = L.T("conn.language.label");
        EditConnectionButton.Content = L.T("settings.editConnection");
        SettingsCloseButton.Content  = L.T("sync.close");
        UpdateDeleteDetail();

        ApplySyncChrome();

        RenderServerStatus();
        ShowDevice(App.Devices.Current);   // recalcula el bloque del iPod en el idioma nuevo
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Cerrar la ventana durante una transferencia no puede dejarla
        // huérfana escribiendo en el iPod: cancelar primero, cerrar después
        // de que el motor efectivamente soltó el dispositivo.
        if (_syncing && _syncPhase == SyncPhase.Running && !_syncCts!.IsCancellationRequested)
        {
            _syncCts.Cancel();
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Sin esto la ventana queda enganchada al vigilante y a los eventos
        // de idioma, que viven mientras viva la app: se acumularían ventanas
        // muertas escuchando eventos.
        App.Devices.Connected -= OnDeviceChanged;
        App.Devices.Disconnected -= OnDeviceGone;
        L.Changed -= ApplyLanguage;
        _syncRepaint?.Stop();
        _syncCts?.Dispose();
        foreach (var bmp in _coverCache.Values) bmp?.Dispose();
        _coverCache.Clear();
        base.OnClosed(e);
    }

    // ============================================================ navegación

    private void ShowMainPanel()
    {
        MainPanel.IsVisible = true;
        SettingsPanel.IsVisible = false;
        SyncPanel.IsVisible = false;
        LibraryPanel.IsVisible = false;
        ShowDevice(App.Devices.Current);
    }

    private void ShowSettingsPanel()
    {
        AutoSyncCheck.IsChecked = _settings.AutoSyncOnConnect;
        StartupCheck.IsChecked = _startup.IsEnabled;
        StartupDetail.Text = _startup.Description;
        SkipConfirmCheck.IsChecked = _settings.AutoSyncSkipDeleteConfirmation;
        SkipConfirmCheck.IsEnabled = _settings.AutoSyncOnConnect;
        DeleteCheck.IsChecked = _settings.DeleteRemovedFiles;
        LanguageBox.SelectedIndex = _settings.Language == AppLanguage.English ? 1 : 0;
        UpdateDeleteDetail();

        MainPanel.IsVisible = false;
        SettingsPanel.IsVisible = true;
        SyncPanel.IsVisible = false;
        LibraryPanel.IsVisible = false;
    }

    // ============================================================ panel principal

    private void OnDeviceChanged(IpodDevice device) =>
        Dispatcher.UIThread.Post(() => { if (MainPanel.IsVisible) ShowDevice(device); });

    private void OnDeviceGone() =>
        Dispatcher.UIThread.Post(() => { if (MainPanel.IsVisible) ShowDevice(null); });

    private void ShowDevice(IpodDevice? device)
    {
        if (device is null)
        {
            IpodStatus.Text = L.T("main.ipod.notfound");
            IpodStatus.Foreground = Idle;
            IpodDetail.Text = "";
            SyncButton.IsEnabled = false;
            LibraryButton.IsEnabled = false;
            return;
        }

        IpodStatus.Text = L.T("main.ipod.found", device.Label, device.MountPath);
        IpodStatus.Foreground = Ok;
        IpodDetail.Text = L.T("main.ipod.freegb", device.FreeGb, device.TotalGb);
        SyncButton.IsEnabled = true;
        LibraryButton.IsEnabled = true;

        ShowLastSync(device);
    }

    /// <summary>Lo que dice el estado guardado en el propio iPod.</summary>
    private void ShowLastSync(IpodDevice device)
    {
        try
        {
            var state = SyncStateService.Load(device.MountPath);
            if (state.LastSyncUtc == default) return;

            IpodDetail.Text += L.T("main.ipod.lastsync",
                state.LastSyncUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                state.TrackCount);
        }
        catch
        {
            // Estado ilegible: no es motivo para no dejar sincronizar.
        }
    }

    private async Task VerifyServerAsync()
    {
        // MainWindow solo se construye con configuración válida en la app
        // real (App enruta a ConnectionWindow si no); probar la conexión con
        // un host vacío no puede tener éxito y solo dispara una tarea de
        // fondo sin sentido (relevante para los tests, que sí construyen
        // MainWindow con settings por default).
        if (!_settings.IsConfigured) return;

        RescanButton.IsEnabled = false;
        ServerStatus.Text = L.T("main.server.verifying");
        try
        {
            using var sftp = new SftpService(_settings);
            var result = await sftp.TestConnectionAsync();
            _lastConnectionResult = result;
            _lastConnectionException = null;
        }
        catch (Exception ex)
        {
            _lastConnectionResult = null;
            _lastConnectionException = ex.Message;
        }

        // Post explícito y no confiar en que el await retome en el hilo de
        // UI: TestConnectionAsync hace su propio Task.Run por dentro, y
        // tocar un TextBlock desde el hilo equivocado es lo que se sospecha
        // detrás de fallas intermitentes de "Unable to locate
        // IFontManagerImpl" en los tests headless.
        Dispatcher.UIThread.Post(() =>
        {
            RenderServerStatus();
            RescanButton.IsEnabled = true;
            RescanButton.Content = L.T("main.rescan");
        });
    }

    private void RenderServerStatus()
    {
        if (_lastConnectionException is { } ex)
        {
            ServerStatus.Text = L.T("common.error", ex);
            ServerStatus.Foreground = Bad;
        }
        else if (_lastConnectionResult is { } r)
        {
            ServerStatus.Text = r.Success ? L.T("main.server.connected") : L.Describe(r);
            ServerStatus.Foreground = r.Success ? Ok : Bad;
        }
        else
        {
            ServerStatus.Text = L.T("main.server.unchecked");
        }
    }

    /// <summary>
    /// Vuelve a probar la conexión sin reiniciar la app ni tocar la
    /// configuración guardada. La sincronización en sí ya vuelve a listar el
    /// servidor entero cada vez que se aprieta "Sincronizar" —música nueva
    /// agregada al servidor ya se detecta sola, sin este botón—; esto es
    /// solo para refrescar el indicador de estado sin esperar a la próxima
    /// sync.
    /// </summary>
    private void OnRescanServer(object? sender, RoutedEventArgs e)
    {
        RescanButton.Content = L.T("main.rescanning");
        _ = VerifyServerAsync();
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs e) => ShowSettingsPanel();

    private void OnSync(object? sender, RoutedEventArgs e)
    {
        // Pasa por App.TryOpenSync y no arranca el panel acá directamente:
        // es el mismo guardia de instancia única que usa la bandeja. Sin él,
        // apretar "Sincronizar" acá y "Sincronizar ahora" en la bandeja a la
        // vez abriría dos transferencias sobre el mismo iPod.
        (Avalonia.Application.Current as App)?.TryOpenSync(autoStart: false);
    }

    // ============================================================ panel de Settings

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        var lang = LanguageBox.SelectedIndex == 1 ? AppLanguage.English : AppLanguage.Spanish;
        L.Choose(lang, _settings);
    }

    private void OnAutoSyncChanged(object? sender, RoutedEventArgs e)
    {
        if (AutoSyncCheck.IsChecked is not { } value) return;

        // Sin auto-sync no hay conexión que auto-confirmar: se apaga y
        // deshabilita junto con él para que no quede prendido sin sentido.
        SkipConfirmCheck.IsEnabled = value;
        if (!value && SkipConfirmCheck.IsChecked == true) SkipConfirmCheck.IsChecked = false;

        if (value == _settings.AutoSyncOnConnect) return;
        _settings.AutoSyncOnConnect = value;
        App.Settings.Save(_settings);
    }

    /// <summary>
    /// Extiende el automatismo del auto-sync también al caso que borra
    /// archivos. Apagado por default: ver AppSettings.AutoSyncSkipDeleteConfirmation.
    /// </summary>
    private void OnSkipConfirmChanged(object? sender, RoutedEventArgs e)
    {
        if (SkipConfirmCheck.IsChecked is not { } value) return;
        if (value == _settings.AutoSyncSkipDeleteConfirmation) return;

        _settings.AutoSyncSkipDeleteConfirmation = value;
        App.Settings.Save(_settings);
    }

    /// <summary>
    /// Con réplica completa, "lo que no está en el servidor se borra" es lo
    /// que hace posible que la sync no acumule cruft indefinidamente. El
    /// costo es real: un álbum que esté en el iPod pero ya no en el servidor
    /// —bajado a mano, o de una fuente distinta a esta biblioteca— se va en
    /// la primera sync. Este toggle es la salida simple mientras no exista
    /// selección parcial (v0.4 del roadmap, deliberadamente después: cambia
    /// la semántica del borrado y necesita un manifiesto persistente).
    /// </summary>
    private void OnDeleteChanged(object? sender, RoutedEventArgs e)
    {
        if (DeleteCheck.IsChecked is not { } value) return;
        UpdateDeleteDetail();
        if (value == _settings.DeleteRemovedFiles) return;

        _settings.DeleteRemovedFiles = value;
        App.Settings.Save(_settings);
    }

    private void UpdateDeleteDetail() =>
        DeleteDetail.Text = DeleteCheck.IsChecked == true
            ? L.T("main.delete.on")
            : L.T("main.delete.off");

    private void OnStartupChanged(object? sender, RoutedEventArgs e)
    {
        if (StartupCheck.IsChecked is not { } value) return;
        if (value == _startup.IsEnabled) return;

        try
        {
            if (value) _startup.Enable(Environment.ProcessPath ?? "");
            else _startup.Disable();
        }
        catch (Exception ex)
        {
            StartupDetail.Text = L.T("main.startupError", ex.Message);
            StartupCheck.IsChecked = _startup.IsEnabled;
            return;
        }

        StartupDetail.Text = _startup.Description;
    }

    private void OnEditConnection(object? sender, RoutedEventArgs e)
    {
        // Editar la conexión sí sigue siendo un cambio de ventana: es la
        // pantalla de setup, no un paso del uso normal del día a día.
        new ConnectionWindow().Show();
        Close();
    }

    private void OnSettingsClose(object? sender, RoutedEventArgs e) => ShowMainPanel();

    // ============================================================ panel de biblioteca

    /// <summary>
    /// Solo lectura de lo que YA está en el iPod — lee
    /// directo Music/ del dispositivo conectado, nunca toca el servidor.
    /// Los ítems se arman a mano acá, sin ItemsControl+bindings, igual que
    /// el resto de la app (ver "sin MVVM por ahora").
    /// </summary>
    private async void OnOpenLibrary(object? sender, RoutedEventArgs e)
    {
        LibraryTitleText.Text        = L.T("library.title");
        LibraryCloseButton.Content   = L.T("library.close");
        LibraryAlbumsModeButton.Content = L.T("library.mode.albums");
        LibrarySongsModeButton.Content  = L.T("library.mode.songs");
        LibraryGridLayoutButton.Content = L.T("library.layout.grid");
        LibraryListLayoutButton.Content = L.T("library.layout.list");
        LibraryBackButton.Content       = L.T("library.back");
        LibraryFixCoversButton.Content  = L.T("library.fixCovers");
        LibraryFixCoversStatus.IsVisible = false;

        _libraryFilter = null;
        _librarySongsMode = false;
        _libraryGridMode = true;
        UpdateLibraryModeButtons();

        MainPanel.IsVisible = false;
        SettingsPanel.IsVisible = false;
        SyncPanel.IsVisible = false;
        LibraryPanel.IsVisible = true;

        var device = App.Devices.Current;
        if (device is null)
        {
            _libraryAlbums = new();
            LibraryItemsHost.Content = null;
            LibraryEmptyText.Text = L.T("library.noDevice");
            LibraryEmptyText.IsVisible = true;
            LibraryFixCoversButton.IsEnabled = false;
            return;
        }

        _libraryMountRoot = device.MountPath;
        _libraryMusicRoot = new IpodLayout(_settings, device.MountPath).MusicRoot;
        LibraryEmptyText.IsVisible = false;
        LibraryItemsHost.Content = null;
        LibraryFixCoversButton.IsEnabled = true;

        try
        {
            // Recorrer Music/ es I/O de disco, no de red, pero igual puede ser
            // lento con miles de archivos — nunca bloquear el hilo de UI.
            _libraryAlbums = await Task.Run(() => new LibraryBrowser().ScanAlbums(_settings, device.MountPath));
            RenderLibrary();
        }
        catch (Exception ex)
        {
            // async void: una excepción que se escape de acá no la cataloga
            // nadie, sube como "unhandled" en un thread del pool y se lleva
            // puesto el proceso entero (ya pasó una vez con la cancelación
            // de las descargas). El caso real es el iPod
            // desconectado a mitad del escaneo.
            _libraryAlbums = new();
            LibraryItemsHost.Content = null;
            LibraryEmptyText.Text = L.T("common.error", ex.Message);
            LibraryEmptyText.IsVisible = true;
        }
    }

    /// <summary>
    /// Corre lo mismo que ya pasa solo después de cada sync (ver
    /// TransferAsync), pero a pedido: sirve para una biblioteca vieja, ya
    /// sincronizada antes de que existiera esto, sin tener que forzar una
    /// sync nueva solo para completar carátulas.
    /// </summary>
    private async void OnLibraryFixCovers(object? sender, RoutedEventArgs e)
    {
        if (_libraryMusicRoot is null || _libraryMountRoot is null) return;

        LibraryFixCoversButton.IsEnabled = false;
        LibraryFixCoversButton.Content = L.T("library.fixCovers.running");
        LibraryFixCoversStatus.IsVisible = false;

        var musicRoot = _libraryMusicRoot;
        var mountRoot = _libraryMountRoot;
        var albums = _libraryAlbums;

        try
        {
            var result = await Task.Run(() => new CoverExtractor().FixMissingCovers(albums, musicRoot));

            // Invalida el caché de las que se acaban de escribir: sin esto
            // seguirían mostrando el placeholder hasta cerrar y reabrir el
            // panel, aunque cover.jpg ya exista en el disco.
            foreach (var bmp in _coverCache.Values) bmp?.Dispose();
            _coverCache.Clear();

            _libraryAlbums = await Task.Run(() => new LibraryBrowser().ScanAlbums(_settings, mountRoot));

            // Si había un álbum filtrado (drill-down), el objeto viejo quedó
            // apuntando al escaneo anterior — sin esto, seguiría mostrando el
            // placeholder aunque su carátula ya se haya escrito recién.
            if (_libraryFilter is { } old)
                _libraryFilter = _libraryAlbums.FirstOrDefault(a => a.RelativePath == old.RelativePath);

            LibraryFixCoversStatus.Text = result.HasWork
                ? L.T("library.fixCovers.result", result.Extracted, result.Skipped)
                : L.T("library.fixCovers.none");
        }
        catch (Exception ex)
        {
            // Ver el comentario de OnOpenLibrary: async void sin catch se
            // lleva puesto el proceso.
            LibraryFixCoversStatus.Text = L.T("common.error", ex.Message);
        }
        finally
        {
            LibraryFixCoversButton.Content = L.T("library.fixCovers");
            LibraryFixCoversButton.IsEnabled = true;
            LibraryFixCoversStatus.IsVisible = true;
        }

        RenderLibrary();
    }

    private void OnLibraryClose(object? sender, RoutedEventArgs e) => ShowMainPanel();

    private void OnLibraryAlbumsMode(object? sender, RoutedEventArgs e)
    {
        _librarySongsMode = false;
        _libraryFilter = null;
        UpdateLibraryModeButtons();
        RenderLibrary();
    }

    private void OnLibrarySongsMode(object? sender, RoutedEventArgs e)
    {
        _librarySongsMode = true;
        UpdateLibraryModeButtons();
        RenderLibrary();
    }

    private void OnLibraryGridLayout(object? sender, RoutedEventArgs e)
    {
        _libraryGridMode = true;
        UpdateLibraryModeButtons();
        RenderLibrary();
    }

    private void OnLibraryListLayout(object? sender, RoutedEventArgs e)
    {
        _libraryGridMode = false;
        UpdateLibraryModeButtons();
        RenderLibrary();
    }

    private void OnLibraryBack(object? sender, RoutedEventArgs e)
    {
        _libraryFilter = null;
        _librarySongsMode = false;
        UpdateLibraryModeButtons();
        RenderLibrary();
    }

    private void UpdateLibraryModeButtons()
    {
        SetActiveClass(LibraryAlbumsModeButton, !_librarySongsMode);
        SetActiveClass(LibrarySongsModeButton, _librarySongsMode);
        SetActiveClass(LibraryGridLayoutButton, _libraryGridMode);
        SetActiveClass(LibraryListLayoutButton, !_libraryGridMode);
    }

    private static void SetActiveClass(Button button, bool active)
    {
        if (active) { if (!button.Classes.Contains("active")) button.Classes.Add("active"); }
        else button.Classes.Remove("active");
    }

    private void RenderLibrary()
    {
        LibraryBackButton.IsVisible = _libraryFilter is not null;
        LibraryTitleText.Text = _libraryFilter?.Name ?? L.T("library.title");

        if (_libraryAlbums.Count == 0)
        {
            LibraryEmptyText.Text = L.T("library.empty");
            LibraryEmptyText.IsVisible = true;
            LibraryItemsHost.Content = null;
            return;
        }
        LibraryEmptyText.IsVisible = false;

        var showSongs = _librarySongsMode || _libraryFilter is not null;
        var albumsToShow = _libraryFilter is { } f ? new[] { f } : _libraryAlbums.ToArray();

        Panel host = _libraryGridMode
            ? new WrapPanel { Orientation = Orientation.Horizontal }
            : new StackPanel { Spacing = 0 };

        if (!showSongs)
        {
            foreach (var album in albumsToShow)
                host.Children.Add(_libraryGridMode ? BuildAlbumTile(album) : BuildAlbumRow(album));
        }
        else
        {
            foreach (var album in albumsToShow)
                foreach (var track in album.Tracks)
                    host.Children.Add(_libraryGridMode ? BuildTrackTile(album, track) : BuildTrackRow(album, track));
        }

        LibraryItemsHost.Content = host;
    }

    private static string TrackCountText(int count) =>
        count == 1 ? L.T("library.trackCount.one") : L.T("library.trackCount", count);

    /// <summary>Carátula suelta del álbum (Rockbox no lee las
    /// embebidas). Se decodifica a un ancho fijo, chico: no hace falta más
    /// resolución que la de un tile, y evita cargar JPEGs enteros en
    /// memoria por cada álbum.</summary>
    private Bitmap? LoadCover(string? relCoverPath)
    {
        if (relCoverPath is null || _libraryMusicRoot is null) return null;
        if (_coverCache.TryGetValue(relCoverPath, out var cached)) return cached;

        Bitmap? bmp = null;
        try
        {
            var full = Path.Combine(_libraryMusicRoot, relCoverPath.Replace('/', Path.DirectorySeparatorChar));
            using var fs = File.OpenRead(full);
            bmp = Bitmap.DecodeToWidth(fs, 240);
        }
        catch
        {
            // JPEG progresivo, archivo corrupto, o el iPod se desconectó a
            // mitad de la lectura: sin carátula, no por eso explota el panel.
        }

        _coverCache[relCoverPath] = bmp;
        return bmp;
    }

    private Control BuildCoverVisual(string? relCoverPath, double size)
    {
        var bmp = LoadCover(relCoverPath);
        if (bmp is not null)
            return new Image { Source = bmp, Width = size, Height = size, Stretch = Stretch.UniformToFill };

        // Sin carátula: un placeholder simple en vez de dejar un hueco en
        // blanco que parezca roto.
        return new Border
        {
            Width = size, Height = size,
            Background = new SolidColorBrush(Color.Parse("#DCDCE1")),
            Child = new TextBlock
            {
                Text = "♪", FontSize = size * 0.4,
                Foreground = new SolidColorBrush(Color.Parse("#8A8A8F")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static Button WrapClickable(Control content, Action onClick)
    {
        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void DrillIntoAlbum(AlbumEntry album)
    {
        _libraryFilter = album;
        RenderLibrary();
    }

    private Control BuildAlbumTile(AlbumEntry album)
    {
        var cover = new Border
        {
            Child = BuildCoverVisual(album.CoverRelativePath, 158),
            ClipToBounds = true,
            CornerRadius = new CornerRadius(9, 9, 0, 0)
        };
        var text = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 10),
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = album.Name, Classes = { "ipod-row-title" },
                                 TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = TrackCountText(album.Tracks.Count), Classes = { "ipod-row-value" } }
            }
        };
        var border = new Border
        {
            Classes = { "ipod-tile" }, Width = 158, Margin = new Thickness(0, 0, 12, 12),
            Child = new StackPanel { Children = { cover, text } }
        };
        return WrapClickable(border, () => DrillIntoAlbum(album));
    }

    private Control BuildAlbumRow(AlbumEntry album)
    {
        var cover = new Border
        {
            Child = BuildCoverVisual(album.CoverRelativePath, 44),
            ClipToBounds = true, CornerRadius = new CornerRadius(7), Width = 44, Height = 44
        };
        var text = new StackPanel
        {
            Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = album.Name, Classes = { "ipod-row-title" } },
                new TextBlock { Text = TrackCountText(album.Tracks.Count), Classes = { "ipod-row-value" } }
            }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(cover, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(cover);
        grid.Children.Add(text);

        var border = new Border { Classes = { "ipod-row" }, Margin = new Thickness(0, 0, 0, 8), Child = grid };
        return WrapClickable(border, () => DrillIntoAlbum(album));
    }

    private Control BuildTrackTile(AlbumEntry album, TrackEntry track)
    {
        var cover = new Border
        {
            Child = BuildCoverVisual(album.CoverRelativePath, 158),
            ClipToBounds = true,
            CornerRadius = new CornerRadius(9, 9, 0, 0)
        };
        var text = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 10),
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = track.Title, Classes = { "ipod-row-title" },
                                 TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = album.Name, Classes = { "ipod-row-value" },
                                 TextTrimming = TextTrimming.CharacterEllipsis }
            }
        };
        return new Border
        {
            Classes = { "ipod-tile" }, Width = 158, Margin = new Thickness(0, 0, 12, 12),
            Child = new StackPanel { Children = { cover, text } }
        };
    }

    private Control BuildTrackRow(AlbumEntry album, TrackEntry track)
    {
        var cover = new Border
        {
            Child = BuildCoverVisual(album.CoverRelativePath, 40),
            ClipToBounds = true, CornerRadius = new CornerRadius(6), Width = 40, Height = 40
        };
        var text = new StackPanel
        {
            Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = track.Title, Classes = { "ipod-row-title" } },
                new TextBlock { Text = album.Name, Classes = { "ipod-row-value" } }
            }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(cover, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(cover);
        grid.Children.Add(text);

        return new Border { Classes = { "ipod-row" }, Margin = new Thickness(0, 0, 0, 8), Child = grid };
    }

    // ============================================================ panel de Sync

    private enum SyncPhase { Analyzing, Confirm, Running, Done }

    private bool _syncing;
    private SyncPhase _syncPhase = SyncPhase.Analyzing;
    private CancellationTokenSource? _syncCts;
    private bool _syncAutoStart;
    private IpodDevice? _syncDevice;

    /// <summary>Los settings de ESTA sincronización, no necesariamente los
    /// mismos que _settings (los que cargó la ventana al construirse). Un
    /// bug real: OnSyncPrimary usaba _settings a secas para transferir, y
    /// si ShowSyncPanel había arrancado con otro AppSettings —como pasa en
    /// los tests, o si App.TryOpenSync recargó el archivo entre el análisis
    /// y la confirmación—, la transferencia se hacía con credenciales
    /// equivocadas (o vacías).</summary>
    private AppSettings? _syncSettings;
    private SyncDiff? _syncDiff;
    private List<RemoteFile>? _syncRemote;
    private List<PlaylistDefinition>? _syncPlaylists;

    /// <summary>Último reporte recibido, todavía sin dibujar. La
    /// transferencia reporta por cada bloque descargado —miles de veces por
    /// segundo— y tocar la UI en cada uno la dejaría congelada. Se guarda el
    /// último y lo dibuja un timer a 10 Hz.</summary>
    private SyncProgress? _syncPending;
    private DispatcherTimer? _syncRepaint;

    /// <summary>Se dispara cuando el panel de sync vuelve al principal
    /// (terminó, se canceló, o el usuario cerró). App lo usa para reanudar
    /// el sondeo del iPod, que se pausa mientras se transfiere.</summary>
    public event Action? SyncPanelClosed;

    private void InitSyncPanelChrome()
    {
        _syncRepaint = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _syncRepaint.Tick += (_, _) => RepaintSync();
    }

    private void ApplySyncChrome()
    {
        SyncSecondaryButton.Content = L.T("sync.cancel");
        SyncPrimaryButton.Content = _syncPhase == SyncPhase.Done ? L.T("sync.close") : L.T("main.sync");
    }

    /// <summary>Punto de entrada único para arrancar una sincronización.
    /// Reemplaza al constructor de la vieja SyncWindow.</summary>
    public void ShowSyncPanel(AppSettings settings, IpodDevice device, bool autoStart)
    {
        _syncing = true;
        _syncAutoStart = autoStart;
        _syncDevice = device;
        _syncSettings = settings;
        _syncCts = new CancellationTokenSource();

        MainPanel.IsVisible = false;
        SettingsPanel.IsVisible = false;
        SyncPanel.IsVisible = true;
        LibraryPanel.IsVisible = false;

        ApplySyncChrome();
        SetSyncPhase(SyncPhase.Analyzing, L.T("sync.phase.analyzing"), L.T("sync.phase.analyzing.detail"));
        Log.Text = "";

        _ = AnalyzeAsync(settings);
    }

    private async Task AnalyzeAsync(AppSettings settings)
    {
        var device = _syncDevice!;
        var ct = _syncCts!.Token;

        try
        {
            var (diff, remote, playlists, catalog) = await Task.Run(() =>
            {
                using var sftp = new SftpService(settings);

                var files = sftp.ListAllFiles(
                    new Progress<string>(m => Dispatcher.UIThread.Post(() => CurrentFile.Text = m)),
                    ct);

                var engine = new SyncEngine(sftp, settings, device.MountPath);
                var defs = engine.FetchPlaylistDefinitions(files, null, ct);
                var d = engine.ComputeDiff(files, null, ct);

                // Si no hay nada que transferir, TransferAsync no se llega a
                // ejecutar nunca (ver más abajo) y con él, el catálogo de
                // playlists tampoco. Sin esto, un iPod que ya tiene toda la
                // música pero perdió (o nunca tuvo) el .m3u8 —por ejemplo
                // tras la corrupción de la SD, o al prender la opción
                // después de la última sync— se queda sin catálogo para
                // siempre: cada sync ve "sin cambios" y nunca lo rearma.
                PlaylistBuildResult? c = null;
                if (!d.HasChanges && settings.BuildPlaylistCatalog)
                    c = new PlaylistCatalogBuilder(settings, device.MountPath).Build(defs, ct);

                return (d, files, defs, c);
            }, ct);

            _syncDiff = diff;
            _syncRemote = remote;
            _syncPlaylists = playlists;

            CurrentFile.Text = "";
            Bar.IsIndeterminate = false;
            Bar.Value = 0;

            // El resumen se arma acá, no con diff.Summary: ese texto viene
            // pre-armado en español desde Core, y Core no tiene que saber en
            // qué idioma mostrarlo (lo usa la CLI tal cual, sin localizar).
            AppendSyncLog(L.T("sync.summary", diff.Added.Count, diff.Updated.Count,
                       diff.Removed.Count, diff.UnchangedCount, diff.BytesToTransfer / 1024d / 1024));
            foreach (var w in diff.Warnings.Take(20)) AppendSyncLog("! " + w);

            if (!diff.HasChanges)
            {
                if (catalog is { } c)
                {
                    AppendSyncLog(L.T("sync.log.playlists", c.PlaylistsWritten, c.EntriesWritten));
                    foreach (var w in c.Warnings.Take(10)) AppendSyncLog("! " + w);
                }
                SetSyncPhase(SyncPhase.Done, L.T("sync.phase.uptodate"), L.T("sync.phase.uptodate.detail"));
                return;
            }

            var detail = L.T("sync.phase.confirm.detail",
                             diff.ToTransfer.Count(), diff.BytesToTransfer / 1024d / 1024 / 1024);

            if (diff.Removed.Count > 0)
                detail += L.T("sync.phase.confirm.delete", diff.Removed.Count);

            SetSyncPhase(SyncPhase.Confirm, L.T("sync.phase.confirm"), detail);

            // Ver SyncDiff.CanAutoApply: el auto-sync copia solo por default;
            // borrar sin preguntar es una opción explícita del usuario.
            if (_syncAutoStart && diff.CanAutoApply(settings.AutoSyncSkipDeleteConfirmation))
                await TransferAsync(settings);
        }
        catch (OperationCanceledException)
        {
            SetSyncPhase(SyncPhase.Done, L.T("sync.phase.cancelled"), L.T("sync.phase.cancelled.detail"));
        }
        catch (Exception ex)
        {
            AppendSyncLog(L.T("common.error", ex.Message));
            SetSyncPhase(SyncPhase.Done, L.T("sync.phase.analyzeError"), ex.Message);
        }
    }

    private async Task TransferAsync(AppSettings settings)
    {
        var device = _syncDevice!;
        var ct = _syncCts!.Token;

        SetSyncPhase(SyncPhase.Running, L.T("sync.phase.running"), L.T("sync.phase.running.detail"));
        _syncRepaint!.Start();

        try
        {
            var result = await Task.Run(async () =>
            {
                using var sftp = new SftpService(settings);
                var engine = new SyncEngine(sftp, settings, device.MountPath);

                var r = await engine.TransferAsync(
                    _syncDiff!, new Progress<SyncProgress>(p => _syncPending = p), ct);

                // El estado va en la raíz del iPod: así sincronizar desde otra
                // máquina funciona igual. Lleva las definiciones de playlist
                // para poder rearmar el catálogo sin conexión.
                SyncStateService.Save(device.MountPath, new SyncState
                {
                    LastSyncUtc     = DateTime.UtcNow,
                    SourceHost      = $"{settings.Username}@{settings.Host}",
                    RemoteMusicPath = settings.RemoteMusicPath,
                    TrackCount      = _syncRemote!.Count(f => !engine.Layout.IsPlaylistDefinition(f.RelativePath)),
                    TotalBytes      = _syncRemote!.Sum(f => f.Size),
                    LastDiffSummary = _syncDiff!.Summary,
                    Playlists       = _syncPlaylists!
                });

                PlaylistBuildResult? catalog = null;
                if (settings.BuildPlaylistCatalog)
                    catalog = new PlaylistCatalogBuilder(settings, device.MountPath)
                              .Build(_syncPlaylists!, ct);

                // Rockbox no lee la carátula embebida de los temas:
                // completa cover.jpg para los álbumes que se
                // quedaron sin ninguna, extrayéndola de uno de sus propios
                // temas ya transferidos. No pisa las que ya existen.
                var albums = new LibraryBrowser().ScanAlbums(settings, device.MountPath);
                var musicRoot = new IpodLayout(settings, device.MountPath).MusicRoot;
                var covers = new CoverExtractor().FixMissingCovers(albums, musicRoot, ct);

                return (Sync: r, Catalog: catalog, Covers: covers);
            }, ct);

            _syncRepaint.Stop();
            RepaintSync();

            Bar.Value = 100;
            CurrentFile.Text = "";

            AppendSyncLog(L.T("sync.log.transferred", result.Sync.Transferred));
            AppendSyncLog(L.T("sync.log.deleted", result.Sync.Deleted));
            if (result.Sync.Failed > 0) AppendSyncLog(L.T("sync.log.failed", result.Sync.Failed));
            foreach (var e in result.Sync.Errors.Take(20)) AppendSyncLog("! " + e);

            if (result.Catalog is { } c)
            {
                AppendSyncLog(L.T("sync.log.playlists", c.PlaylistsWritten, c.EntriesWritten));
                foreach (var w in c.Warnings.Take(10)) AppendSyncLog("! " + w);
            }

            if (result.Covers.Extracted > 0)
                AppendSyncLog(L.T("sync.log.covers", result.Covers.Extracted));

            SetSyncPhase(SyncPhase.Done,
                result.Sync.Failed > 0 ? L.T("sync.phase.done.errors") : L.T("sync.phase.done"),
                L.T("sync.phase.done.detail"));
        }
        catch (OperationCanceledException)
        {
            _syncRepaint.Stop();
            AppendSyncLog(L.T("sync.log.cancelledByUser"));
            SetSyncPhase(SyncPhase.Done, L.T("sync.phase.cancelled"), L.T("sync.phase.cancelled.detail2"));
        }
        catch (Exception ex)
        {
            _syncRepaint.Stop();
            AppendSyncLog(L.T("common.error", ex.Message));
            SetSyncPhase(SyncPhase.Done, L.T("sync.phase.syncError"), ex.Message);
        }
    }

    private void RepaintSync()
    {
        if (_syncPending is not { } p) return;
        _syncPending = null;

        Bar.Value = p.Percent;
        CurrentFile.Text = p.CurrentFile;

        var eta = p.Eta is { } t ? $"  ETA {t:hh\\:mm\\:ss}" : "";
        Counter.Text = $"{p.FileIndex}/{p.TotalFiles}{eta}";
    }

    private void SetSyncPhase(SyncPhase phase, string title, string detail)
    {
        _syncPhase = phase;
        PhaseTitle.Text = title;
        PhaseDetail.Text = detail;

        Bar.IsIndeterminate = phase == SyncPhase.Analyzing;

        SyncPrimaryButton.IsEnabled = phase is SyncPhase.Confirm or SyncPhase.Done;
        SyncPrimaryButton.Content = phase == SyncPhase.Done ? L.T("sync.close") : L.T("main.sync");
        SyncSecondaryButton.IsEnabled = phase != SyncPhase.Done;
    }

    private void AppendSyncLog(string line)
    {
        Log.Text = string.IsNullOrEmpty(Log.Text) ? line : Log.Text + "\n" + line;
        LogScroll.ScrollToEnd();
    }

    private async void OnSyncPrimary(object? sender, RoutedEventArgs e)
    {
        if (_syncPhase == SyncPhase.Done) { ReturnToMainFromSync(); return; }
        if (_syncPhase == SyncPhase.Confirm) await TransferAsync(_syncSettings!);
    }

    private void OnSyncSecondary(object? sender, RoutedEventArgs e)
    {
        // Durante la transferencia, cancelar corta el archivo en curso. El
        // .tmp se descarta, así que nunca queda un archivo truncado.
        _syncCts?.Cancel();
        if (_syncPhase != SyncPhase.Running) ReturnToMainFromSync();
    }

    private void ReturnToMainFromSync()
    {
        _syncing = false;
        _syncRepaint?.Stop();
        _syncCts?.Dispose();
        _syncCts = null;
        _syncSettings = null;
        _syncDiff = null;
        _syncRemote = null;
        _syncPlaylists = null;

        ShowMainPanel();
        SyncPanelClosed?.Invoke();
    }

    /// <summary>Si hay una sincronización en curso ahora mismo (para el
    /// guardia de instancia única de App.TryOpenSync).</summary>
    public bool IsSyncing => _syncing;
}
