namespace IpodSync.Core.Models;

/// <summary>
/// La definición de una playlist tal como vino del servidor: el contenido
/// crudo del .m3u.
///
/// Se guarda acá y NO se copia el .m3u al iPod a propósito. Si estuviera en
/// el dispositivo, Rockbox lo mostraría como una playlist más y al abrirla
/// estaría rota: sus rutas relativas fueron escritas para el árbol del
/// servidor, no para el del reproductor.
/// </summary>
public class PlaylistDefinition
{
    /// <summary>Nombre visible, ya sin el UUID de TideKeeper.</summary>
    public string Name { get; set; } = "";

    /// <summary>Carpeta original en el servidor, con UUID. Solo informativa.</summary>
    public string RemoteFolder { get; set; } = "";

    /// <summary>Las líneas del .m3u, tal cual, sin resolver.</summary>
    public List<string> Lines { get; set; } = new();
}

/// <summary>
/// Estado de la última sincronización.
///
/// Vive en la RAÍZ del iPod y no en la PC, para que la sincronización
/// funcione igual desde cualquier máquina. El nombre empieza con punto
/// porque Rockbox oculta los archivos que empiezan así (igual que
/// `.rockbox`): el usuario no tiene por qué verlo al navegar.
/// </summary>
public class SyncState
{
    public const string FileName = ".ipod-sync-state.json";

    public int Version { get; set; } = 1;

    public DateTime LastSyncUtc { get; set; }

    /// <summary>De dónde salió esta biblioteca. Si cambia, conviene avisar
    /// antes de borrar: puede ser otro servidor y no una biblioteca vaciada.</summary>
    public string SourceHost { get; set; } = "";

    public string RemoteMusicPath { get; set; } = "";

    public int TrackCount { get; set; }

    public long TotalBytes { get; set; }

    public string LastDiffSummary { get; set; } = "";

    /// <summary>Lo que permite rearmar el catálogo sin conexión al servidor.</summary>
    public List<PlaylistDefinition> Playlists { get; set; } = new();
}
