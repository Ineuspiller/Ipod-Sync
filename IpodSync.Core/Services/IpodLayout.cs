using System.Text.RegularExpressions;
using IpodSync.Core.Models;

namespace IpodSync.Core.Services;

/// <summary>
/// Decide dónde vive cada archivo del servidor dentro del iPod. Es la ÚNICA
/// autoridad sobre el tema: el motor de sync y el generador de playlists
/// tienen que coincidir exactamente, si no las playlists apuntan a rutas que
/// no existen.
///
/// El servidor tiene todo bajo una sola carpeta de música, con las playlists
/// de TideKeeper en un subdirectorio. El iPod las separa:
///
///     musica/CD 01/tema.flac                  ->  /Music/CD 01/tema.flac
///     musica/Playlist/Auto [uuid]/tema.flac   ->  /Playlists/Auto/tema.flac
///
/// La razón es el navegador de archivos de Rockbox, que muestra el disco tal
/// cual es: con las playlists colgando de Music/ se mezclan con los álbumes.
/// (`database.ignore` NO alcanza para esto: solo excluye del índice de la
/// base de datos, no de la vista de archivos.)
/// </summary>
public class IpodLayout
{
    /// <summary>TideKeeper nombra las carpetas `{Nombre} [{UUID}]`. En el iPod
    /// se ve solo el nombre. El patrón exige un UUID de verdad: un `[Live]`
    /// legítimo se conserva.</summary>
    private static readonly Regex TrailingUuid = new(
        @"\s*\[[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\]$",
        RegexOptions.Compiled);

    private readonly AppSettings _settings;

    public IpodLayout(AppSettings settings, string mountRoot)
    {
        _settings = settings;
        MountRoot = Path.GetFullPath(mountRoot);
        MusicRoot = Path.Combine(MountRoot, settings.IpodMusicFolder);
        CatalogRoot = Path.Combine(MountRoot, settings.IpodPlaylistFolder);
    }

    public string MountRoot { get; }

    /// <summary>`<iPod>/Music`: solo álbumes.</summary>
    public string MusicRoot { get; }

    /// <summary>`<iPod>/Playlists`: los .m3u8 del catálogo y, en una carpeta
    /// por playlist, las pistas que no pertenecen a ningún álbum.</summary>
    public string CatalogRoot { get; }

    /// <summary>Las dos únicas raíces donde el sync puede escribir o borrar.</summary>
    public IReadOnlyList<string> WritableRoots => new[] { MusicRoot, CatalogRoot };

    /// <summary>
    /// Los nombres de carátula suelta que Rockbox busca en la carpeta del
    /// álbum (no lee las embebidas). Única autoridad sobre el
    /// tema: los comparten <see cref="LibraryBrowser"/> para mostrarlas,
    /// <see cref="CoverExtractor"/> para escribirlas, y
    /// <see cref="IsIpodOnly"/> para no borrarlas.
    /// </summary>
    public static readonly IReadOnlyList<string> CoverFileNames = new[]
    {
        "cover.jpg", "cover.jpeg", "folder.jpg", "folder.jpeg", "cover.png", "cover.bmp"
    };

    public static bool IsCoverFileName(string fileName) =>
        CoverFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Nombre visible de una playlist a partir de su carpeta remota.</summary>
    public static string PlaylistName(string remoteFolderSegment) =>
        FatPath.SanitizeSegment(TrailingUuid.Replace(remoteFolderSegment, "").Trim());

    /// <summary>
    /// Ruta del servidor (relativa a RemoteMusicPath) → ruta dentro del iPod,
    /// relativa a la raíz del dispositivo y ya saneada para FAT32.
    /// </summary>
    public string MapToIpodRelative(string remoteRelativePath)
    {
        var parts = remoteRelativePath.Replace('\\', '/')
                                      .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0) return _settings.IpodMusicFolder;

        var isPlaylist = string.Equals(parts[0], _settings.PlaylistSourceFolder,
                                       StringComparison.OrdinalIgnoreCase);

        if (!isPlaylist)
            return _settings.IpodMusicFolder + "/" + Join(parts, 0);

        // Un archivo suelto directamente en Playlist/ (por ejemplo un readme):
        // no tiene carpeta propia, va a la raíz del catálogo.
        if (parts.Length == 2)
            return _settings.IpodPlaylistFolder + "/" + FatPath.SanitizeSegment(parts[1]);

        // Playlist/{Nombre} [uuid]/resto...  ->  Playlists/{Nombre}/resto...
        var name = PlaylistName(parts[1]);
        return _settings.IpodPlaylistFolder + "/" + name +
               (parts.Length > 2 ? "/" + Join(parts, 2) : "");
    }

    private static string Join(string[] parts, int from) =>
        string.Join('/', parts.Skip(from).Select(FatPath.SanitizeSegment));

    /// <summary>
    /// ¿Es el .m3u que DEFINE una playlist? Estos no se copian al iPod: su
    /// contenido va al estado en la raíz. Si estuvieran en el dispositivo,
    /// Rockbox los mostraría como playlists rotas, porque sus rutas fueron
    /// escritas para el árbol del servidor.
    /// </summary>
    public bool IsPlaylistDefinition(string remoteRelativePath)
    {
        var parts = remoteRelativePath.Replace('\\', '/')
                                      .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 3
            && parts[0].Equals(_settings.PlaylistSourceFolder, StringComparison.OrdinalIgnoreCase)
            && (parts[^1].EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)
             || parts[^1].EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>La carpeta remota (con UUID) a la que pertenece una ruta de
    /// playlist. Null si la ruta no está dentro de una.</summary>
    public string? PlaylistFolderOf(string remoteRelativePath)
    {
        var parts = remoteRelativePath.Replace('\\', '/')
                                      .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 3
            && parts[0].Equals(_settings.PlaylistSourceFolder, StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : null;
    }

    /// <summary>
    /// Resuelve una línea de un .m3u contra la carpeta que lo contiene, en el
    /// ÁRBOL DEL SERVIDOR, sin tocar el disco. Devuelve la ruta relativa a la
    /// carpeta de música remota, o null si se escapa de ella.
    ///
    /// Se resuelve contra el árbol remoto y no contra el del iPod porque las
    /// rutas del .m3u fueron escritas allá: un `../../CD 01/x.flac` cuenta
    /// saltos desde `musica/Playlist/{carpeta}/`, que ya no es donde el
    /// archivo vive en el reproductor.
    /// </summary>
    public string? ResolveM3uLine(string playlistFolderSegment, string line)
    {
        var stack = new List<string> { _settings.PlaylistSourceFolder, playlistFolderSegment };

        foreach (var part in line.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..")
            {
                if (stack.Count == 0) return null;   // se escapa de la biblioteca
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }

        return stack.Count == 0 ? null : string.Join('/', stack);
    }

    /// <summary>
    /// Archivos que viven SOLO en el iPod: el borrado del sync no debe
    /// tocarlos por más que no existan en el servidor.
    /// </summary>
    public bool IsIpodOnly(string mountRelativePath)
    {
        var parts = mountRelativePath.Replace('\\', '/')
                                     .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var name = parts[^1];

        // Excluye una carpeta y sus subcarpetas del índice de la base de datos.
        if (name.Equals("database.ignore", StringComparison.OrdinalIgnoreCase)) return true;

        // Las carátulas sueltas: si el servidor tiene una, llega por el
        // camino normal y ni pasa por acá (está en `expected`). Si NO la
        // tiene, es una que extrajo CoverExtractor de los tags y vive solo
        // en el iPod. Sin esta excepción, el borrado se la lleva en la
        // sync siguiente y la de después la vuelve a extraer: escrituras
        // de más, para siempre, sobre una SD que ya se corrompió dos veces.
        //
        // El costo es que una carátula borrada A PROPÓSITO en el servidor
        // se queda en el iPod. Barato al lado de la alternativa: el álbum
        // sigue mostrando una imagen en vez de ninguna.
        if (IsCoverFileName(name)) return true;

        // Los .m3u8 en la raíz del catálogo: los genera esta app, o los grabó
        // el usuario a mano desde Rockbox. Ninguno existe en el servidor.
        return parts.Length == 2
            && parts[0].Equals(_settings.IpodPlaylistFolder, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }
}
