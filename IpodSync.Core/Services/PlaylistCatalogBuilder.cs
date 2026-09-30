using System.Text;
using IpodSync.Core.Devices;
using IpodSync.Core.Models;

namespace IpodSync.Core.Services;

public record PlaylistBuildResult(
    int PlaylistsWritten,
    int EntriesWritten,
    int EntriesDropped,
    int StaleRemoved,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Arma el Playlist Catalogue de Rockbox después de sincronizar.
///
/// Los .m3u del servidor traen rutas RELATIVAS a la carpeta de la playlist
/// (`../../CD 01/tema.flac`). Rockbox no las entiende: busca las playlists en
/// /Playlists de la raíz y necesita rutas ABSOLUTAS desde la raíz del
/// reproductor. Este paso es la traducción entre las dos convenciones.
///
/// Las definiciones NO se leen del iPod sino del estado guardado en la raíz:
/// un .m3u de origen en el dispositivo se vería como una playlist más y al
/// abrirla estaría rota, porque sus rutas apuntan al árbol del servidor.
///
/// Queda así en el iPod:
///
///     /Playlists/Auto.m3u8      <- el catálogo, rutas absolutas
///     /Playlists/Auto/          <- las pistas que no están en ningún álbum
/// </summary>
public class PlaylistCatalogBuilder
{
    /// <summary>
    /// Marca que identifica los .m3u8 generados por esta app. Sirve para poder
    /// limpiar los que quedaron de una corrida anterior sin llevarse puestas
    /// las playlists que el usuario haya grabado a mano desde Rockbox.
    /// </summary>
    private const string Marker = "# generado por IpodSync - no editar a mano";

    /// <summary>
    /// Un archivo con este nombre excluye la carpeta y sus subcarpetas del
    /// índice de Rockbox. Va en la raíz del catálogo para que las pistas
    /// sueltas de las playlists no aparezcan como álbumes falsos al navegar
    /// por artista/álbum. Después hay que hacer *Initialize now* (no
    /// *Update now*) en el iPod.
    /// </summary>
    private const string DatabaseIgnore = "database.ignore";

    /// <summary>UTF-8 SIN BOM: Rockbox no digiere el BOM en los .m3u8.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IpodLayout _layout;

    public PlaylistCatalogBuilder(AppSettings settings, IpodDevice device)
        : this(new IpodLayout(settings, device.MountPath)) { }

    public PlaylistCatalogBuilder(AppSettings settings, string mountRoot)
        : this(new IpodLayout(settings, mountRoot)) { }

    public PlaylistCatalogBuilder(IpodLayout layout) => _layout = layout;

    public string CatalogRoot => _layout.CatalogRoot;

    /// <summary>Rearma el catálogo con las definiciones que quedaron guardadas
    /// en el iPod. No necesita conexión al servidor.</summary>
    public PlaylistBuildResult Build(CancellationToken ct = default) =>
        Build(SyncStateService.Load(_layout.MountRoot).Playlists, ct);

    public PlaylistBuildResult Build(
        IReadOnlyList<PlaylistDefinition> playlists, CancellationToken ct = default)
    {
        var warnings = new List<string>();

        if (!Directory.Exists(_layout.CatalogRoot))
            return new PlaylistBuildResult(0, 0, 0, 0,
                new[] { $"No existe la carpeta de playlists: {_layout.CatalogRoot}" });

        EnsureDatabaseIgnore(warnings);

        // Índice de lo que realmente hay en el iPod. No alcanza con
        // File.Exists: la ruta del .m3u trae el nombre del SERVIDOR, que en
        // FAT32 puede haber quedado saneado y con otra caja de mayúsculas.
        var present = BuildFileIndex(ct);

        var written = 0;
        var entriesWritten = 0;
        var entriesDropped = 0;
        var generated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var playlist in playlists.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            var (entries, dropped) = ResolveEntries(playlist, present, warnings);
            entriesDropped += dropped;

            if (entries.Count == 0)
            {
                warnings.Add($"Playlist vacía, se omite: {playlist.Name}");
                continue;
            }

            var dest = Path.Combine(_layout.CatalogRoot, playlist.Name + ".m3u8");
            WriteCatalogFile(dest, entries);

            generated.Add(dest);
            entriesWritten += entries.Count;
            written++;
        }

        var stale = RemoveStaleCatalogFiles(generated, warnings, ct);

        return new PlaylistBuildResult(written, entriesWritten, entriesDropped, stale, warnings);
    }

    // ------------------------------------------------------------ interno

    /// <summary>Todo lo que el sync administra, indexado por clave de FAT32.</summary>
    private Dictionary<string, string> BuildFileIndex(CancellationToken ct)
    {
        var index = new Dictionary<string, string>();

        foreach (var root in _layout.WritableRoots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (path.EndsWith(SftpService.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                var rel = Path.GetRelativePath(_layout.MountRoot, path).Replace('\\', '/');
                index[FatPath.ComparisonKey(rel)] = rel;
            }
        }
        return index;
    }

    private (List<string> Entries, int Dropped) ResolveEntries(
        PlaylistDefinition playlist,
        Dictionary<string, string> present,
        List<string> warnings)
    {
        var entries = new List<string>();
        var dropped = 0;

        foreach (var raw in playlist.Lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;   // #EXTM3U, #EXTINF

            // Las rutas del .m3u cuentan saltos desde el árbol del SERVIDOR,
            // que no es donde el archivo terminó en el reproductor.
            var remoteRel = _layout.ResolveM3uLine(playlist.Name, line);
            if (remoteRel is null)
            {
                warnings.Add($"{playlist.Name}: ruta fuera de la biblioteca, se descarta: {line}");
                dropped++;
                continue;
            }

            var mountRel = _layout.MapToIpodRelative(remoteRel);

            if (!present.TryGetValue(FatPath.ComparisonKey(mountRel), out var actual))
            {
                dropped++;   // el tema no llegó al iPod: la entrada no sirve
                continue;
            }

            entries.Add("/" + actual);
        }

        return (entries, dropped);
    }

    private static void WriteCatalogFile(string dest, List<string> entries)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n").Append(Marker).Append('\n');
        foreach (var e in entries) sb.Append(e).Append('\n');

        // Se escribe a .tmp y se renombra, igual que las descargas: un corte
        // a mitad no puede dejar una playlist truncada que Rockbox lea igual.
        var tmp = dest + SftpService.TempSuffix;
        File.WriteAllText(tmp, sb.ToString(), Utf8NoBom);
        File.Move(tmp, dest, overwrite: true);
    }

    /// <summary>
    /// Borra los .m3u8 de corridas anteriores cuya playlist de origen ya no
    /// existe. Solo toca los que llevan nuestra marca: las playlists que el
    /// usuario grabó a mano desde Rockbox viven en la misma carpeta.
    /// </summary>
    private int RemoveStaleCatalogFiles(
        HashSet<string> generated, List<string> warnings, CancellationToken ct)
    {
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(_layout.CatalogRoot, "*.m3u8"))
        {
            ct.ThrowIfCancellationRequested();
            if (generated.Contains(file)) continue;

            try
            {
                if (!File.ReadLines(file, Utf8NoBom).Take(2).Any(l => l == Marker)) continue;
                File.Delete(file);
                removed++;
            }
            catch (Exception ex)
            {
                warnings.Add($"No se pudo limpiar {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return removed;
    }

    private void EnsureDatabaseIgnore(List<string> warnings)
    {
        var path = Path.Combine(_layout.CatalogRoot, DatabaseIgnore);
        if (File.Exists(path)) return;

        try
        {
            File.WriteAllText(path, "", Utf8NoBom);
            warnings.Add($"Creado {DatabaseIgnore} en {Path.GetFileName(_layout.CatalogRoot)}/: " +
                         "hay que hacer *Initialize now* en el iPod (no *Update now*).");
        }
        catch (Exception ex)
        {
            warnings.Add($"No se pudo crear {DatabaseIgnore}: {ex.Message}");
        }
    }
}
