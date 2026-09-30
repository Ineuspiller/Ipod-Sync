using IpodSync.Core.Models;

namespace IpodSync.Core.Services;

/// <summary>Una pista dentro de un álbum. <see cref="RelativePath"/> es
/// relativa a la raíz del iPod (mismo formato que usa el resto de la app),
/// para poder abrirla directo sin volver a resolver la ruta.</summary>
public record TrackEntry(string RelativePath, string Title, long Size);

/// <summary>Una carpeta de <c>Music/</c> que contiene al menos un archivo de
/// audio. <see cref="CoverRelativePath"/> es null si no hay carátula suelta
/// (ver Rockbox: no lee las embebidas, busca cover.jpg/folder.jpg/cover.bmp
/// en la misma carpeta).</summary>
public record AlbumEntry(
    string RelativePath,
    string Name,
    string? CoverRelativePath,
    IReadOnlyList<TrackEntry> Tracks);

/// <summary>
/// Lee lo que YA está en el iPod para mostrarlo en el browser de biblioteca.
/// A propósito no toca el servidor ni SFTP: es solo lectura de lo que el
/// dispositivo ya tiene, así que anda sin conexión y sin credenciales.
///
/// No lee tags (ID3/FLAC): el título de cada pista sale del nombre de
/// archivo. Agregar una librería de tags es una dependencia más y este
/// browser es de solo lectura — el nombre de archivo ya suele traer
/// "NN - Título" en esta biblioteca.
/// </summary>
public class LibraryBrowser
{
    private static readonly string[] AudioExtensions =
        { ".flac", ".mp3", ".m4a", ".aac", ".ogg", ".wav", ".wma" };

    /// <summary>
    /// Recorre `<mount>/Music` y arma un álbum por cada carpeta que tenga
    /// audio directamente adentro (no por subcarpetas: una carpeta con solo
    /// subcarpetas no es un álbum, es un contenedor — ej. si algún día se
    /// agrupara por artista). Suficiente para la biblioteca real de este
    /// proyecto, que es plana: `Music/{Álbum}/tema.flac`.
    /// </summary>
    public List<AlbumEntry> ScanAlbums(AppSettings settings, string mountRoot,
                                        CancellationToken ct = default)
    {
        var musicRoot = new IpodLayout(settings, mountRoot).MusicRoot;
        var result = new List<AlbumEntry>();
        if (!Directory.Exists(musicRoot)) return result;

        Walk(musicRoot, musicRoot, result, ct);
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private void Walk(string musicRoot, string dir, List<AlbumEntry> result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(dir); }
        catch { return; }   // se desconectó a mitad del escaneo

        foreach (var sub in subdirs)
            Walk(musicRoot, sub, result, ct);

        var tracks = new List<TrackEntry>();
        string? cover = null;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); }
        catch { return; }

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(path);

            if (cover is null && IpodLayout.IsCoverFileName(name))
            {
                cover = ToRelative(musicRoot, path);
                continue;
            }

            if (!AudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;

            FileInfo fi;
            try { fi = new FileInfo(path); }
            catch { continue; }

            tracks.Add(new TrackEntry(ToRelative(musicRoot, path),
                                       Path.GetFileNameWithoutExtension(path), fi.Length));
        }

        if (tracks.Count == 0) return;   // carpeta contenedora, no un álbum

        tracks.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));

        // Temas sueltos directamente en Music/: GetRelativePath devuelve "."
        // para la raíz contra sí misma, que después ensucia todas las rutas
        // que se arman con esto (Path.Combine(root, "./x") y el álbum se
        // mostraría llamado ".").
        var relDir = ToRelative(musicRoot, dir);
        if (relDir == ".") relDir = "";

        var albumName = relDir.Length == 0
            ? Path.GetFileName(musicRoot.TrimEnd(Path.DirectorySeparatorChar, '/'))
            : Path.GetFileName(dir);

        result.Add(new AlbumEntry(relDir, albumName, cover, tracks));
    }

    private static string ToRelative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
