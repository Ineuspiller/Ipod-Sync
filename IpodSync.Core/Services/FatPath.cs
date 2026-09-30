using System.Text;

namespace IpodSync.Core.Services;

/// <summary>
/// Traduce rutas del servidor (ext4: distingue mayúsculas, acepta casi
/// cualquier byte UTF-8) a rutas válidas en el iPod (FAT32: no distingue
/// mayúsculas, juego de caracteres restringido).
///
/// Tiene que ser determinística y estar en un solo lugar: el motor de sync
/// y el generador de playlists tienen que llegar al mismo nombre para el
/// mismo archivo. Si divergen, las playlists apuntan a archivos que no
/// existen y Rockbox las muestra vacías.
/// </summary>
public static class FatPath
{
    /// <summary>Prohibidos en FAT32. La barra se trata aparte: separa segmentos.</summary>
    private static readonly char[] Illegal = { '\\', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>Con qué se reemplaza un carácter prohibido.</summary>
    public const char Replacement = '_';

    /// <summary>
    /// Nombres que Windows reserva para dispositivos: no puede crear un
    /// archivo que se llame así, ni con extensión. Improbable en una
    /// biblioteca de música, pero el fallo sería incomprensible.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Limpia un segmento suelto: un nombre de carpeta o de archivo.</summary>
    public static string SanitizeSegment(string segment)
    {
        if (string.IsNullOrEmpty(segment)) return segment;

        var sb = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            // Los caracteres de control rompen FAT32 aunque no estén en la
            // lista clásica de prohibidos.
            if (c < ' ' || Array.IndexOf(Illegal, c) >= 0) sb.Append(Replacement);
            else sb.Append(c);
        }

        // FAT32 no tolera punto ni espacio al final: Windows los recorta solo
        // al crear, y después el nombre en disco no coincide con el que
        // pedimos, así que el diff daría el archivo por faltante para siempre.
        var result = sb.ToString().TrimEnd(' ', '.');
        if (result.Length == 0) return Replacement.ToString();

        var stem = Path.GetFileNameWithoutExtension(result);
        if (ReservedNames.Contains(stem)) result = Replacement + result;

        return result;
    }

    /// <summary>
    /// Limpia una ruta relativa completa, segmento por segmento, y devuelve
    /// los segmentos separados por '/' (la convención que usa Rockbox).
    /// </summary>
    public static string SanitizeRelativePath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('/', parts.Select(SanitizeSegment));
    }

    /// <summary>
    /// Clave para comparar dos rutas. Dos razones para no comparar los
    /// strings crudos: FAT32 no distingue mayúsculas (Song.flac y song.flac
    /// son el MISMO archivo en el iPod), y el mismo nombre en japonés o con
    /// acentos puede venir en distinta forma de normalización Unicode desde
    /// el servidor, lo que haría que el diff lo viera como un archivo nuevo
    /// en cada corrida.
    /// </summary>
    public static string ComparisonKey(string relativePath) =>
        relativePath.Replace('\\', '/')
                    .Normalize(NormalizationForm.FormC)
                    .ToUpperInvariant();

    /// <summary>Convierte una ruta relativa a la ruta local del sistema.</summary>
    public static string ToLocalPath(string root, string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }
}
