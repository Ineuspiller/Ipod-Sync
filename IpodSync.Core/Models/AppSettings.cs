using System.Globalization;

namespace IpodSync.Core.Models;

public enum AuthMode
{
    Password,
    PrivateKey
}

public enum AppLanguage
{
    Spanish,
    English
}

/// <summary>
/// Configuración persistente de la aplicación.
/// Se guarda como JSON en la carpeta de datos del usuario.
/// La contraseña nunca se guarda en claro: ver SettingsService.
/// </summary>
public class AppSettings
{
    // --- Servidor ---
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";

    public AuthMode AuthMode { get; set; } = AuthMode.Password;

    /// <summary>Contraseña cifrada (DPAPI en Windows). Nunca en claro.</summary>
    public string? EncryptedPassword { get; set; }

    /// <summary>Ruta a la clave privada OpenSSH, si AuthMode = PrivateKey.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Passphrase de la clave, cifrada. Vacío si no tiene.</summary>
    public string? EncryptedKeyPassphrase { get; set; }

    // --- Rutas ---
    /// <summary>Carpeta de música en el servidor.</summary>
    public string RemoteMusicPath { get; set; } = "";

    /// <summary>Subcarpeta destino dentro del iPod.</summary>
    public string IpodMusicFolder { get; set; } = "Music";

    /// <summary>Carpeta del Playlist Catalogue de Rockbox (raíz del iPod).</summary>
    public string IpodPlaylistFolder { get; set; } = "Playlists";

    /// <summary>Subcarpeta con los .m3u de origen, dentro de la música.</summary>
    public string PlaylistSourceFolder { get; set; } = "Playlist";

    /// <summary>
    /// Solo importa la primera vez: si no hay settings.json todavía, el
    /// idioma de arranque sale del sistema operativo en vez de asumir uno
    /// fijo. Piloto de una instalación nueva en la máquina de otra persona
    /// que no lea español: tiene que poder usar la app antes de encontrar
    /// el selector de idioma. Una vez que se guarda, esto no se vuelve a
    /// tocar solo.
    /// </summary>
    public AppLanguage Language { get; set; } = DetectSystemLanguage();

    private static AppLanguage DetectSystemLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("es", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Spanish
            : AppLanguage.English;

    // --- Comportamiento ---
    public bool AutoSyncOnConnect { get; set; } = true;
    public bool DeleteRemovedFiles { get; set; } = true;
    public bool BuildPlaylistCatalog { get; set; } = true;
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Solo tiene efecto junto con AutoSyncOnConnect. Apagado por default:
    /// sin esto, el auto-sync al conectar el iPod YA transfiere sin pedir
    /// confirmación cuando el diff no borra nada (ver
    /// SyncDiff.RequiresConfirmation). Prenderlo extiende ese automatismo
    /// también a los diffs que SÍ borran archivos — el caso que en la
    /// práctica causó que un usuario perdiera un álbum que tenía en el iPod
    /// y no en el servidor. Es una decisión consciente del usuario, no algo
    /// que deba quedar prendido por accidente.
    /// </summary>
    public bool AutoSyncSkipDeleteConfirmation { get; set; } = false;

    /// <summary>Patrones que no se copian al iPod.</summary>
    public List<string> ExcludePatterns { get; set; } = new()
    {
        "*.tidekeeper.json",
        "*.m3u.bak",
        "failed-tracks.txt",
        ".ndignore",
        "*.nfo",
        "*.log",
        "*.cue",
        "*.accurip"
    };

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(RemoteMusicPath);
}
