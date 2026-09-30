using SkiaSharp;

namespace IpodSync.Core.Services;

public record CoverExtractionResult(int Extracted, int Skipped, IReadOnlyList<string> Errors)
{
    public bool HasWork => Extracted > 0 || Skipped > 0;
}

/// <summary>
/// Completa las carátulas que faltan en el iPod extrayendo la que ya viene
/// EMBEBIDA en uno de los temas del álbum. Rockbox no la lee ahí adentro —
/// busca un archivo suelto en la carpeta — pero Navidrome
/// sí, así que la fuente casi siempre existe, solo no está en el formato
/// que Rockbox necesita.
///
/// Es justo lo que hace <c>extract_covers.py</c> a mano en el servidor,
/// pero corrido acá mismo sobre lo que YA está en el iPod: no hace falta
/// tocar el servidor ni el SFTP (que hoy solo sabe bajar, no subir), y el
/// resultado queda igual de disponible la próxima vez que se abra la
/// biblioteca offline.
/// </summary>
public class CoverExtractor
{
    /// <summary>La pantalla del iPod Classic es 320×240; más resolución que
    /// esto solo hace que Rockbox tarde en abrir el álbum.</summary>
    private const int MaxDimension = 500;

    /// <summary>
    /// Procesa los álbumes SIN carátula (los que ya tienen una, ni se
    /// tocan). Un álbum sin ninguna imagen embebida en ninguno de sus temas
    /// cuenta como "sin resolver", no como error: no hay nada raro, esa
    /// biblioteca en particular no la trae.
    /// </summary>
    public CoverExtractionResult FixMissingCovers(
        IEnumerable<AlbumEntry> albums, string musicRoot, CancellationToken ct = default)
    {
        var extracted = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var album in albums)
        {
            ct.ThrowIfCancellationRequested();
            if (album.CoverRelativePath is not null) continue;   // ya tiene, no tocar

            if (TryExtractOne(album, musicRoot, errors)) extracted++;
            else skipped++;
        }

        return new CoverExtractionResult(extracted, skipped, errors);
    }

    private bool TryExtractOne(AlbumEntry album, string musicRoot, List<string> errors)
    {
        foreach (var track in album.Tracks)
        {
            var trackPath = Path.Combine(musicRoot, ToLocalPath(track.RelativePath));

            TagLib.IPicture? pic;
            try
            {
                using var file = TagLib.File.Create(trackPath);
                pic = file.Tag.Pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                      ?? file.Tag.Pictures.FirstOrDefault();
            }
            catch (Exception ex)
            {
                // Tag ilegible en ESTE tema no descarta el álbum: puede que
                // el siguiente sí tenga una carátula legible.
                errors.Add($"{track.RelativePath}: {ex.Message}");
                continue;
            }

            if (pic is null) continue;   // este tema no trae carátula, probar el siguiente

            var albumDir = Path.Combine(musicRoot, ToLocalPath(album.RelativePath));
            var coverPath = Path.Combine(albumDir, "cover.jpg");

            try
            {
                WriteResizedBaselineJpeg(pic.Data.ToArray(), coverPath);
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"{album.Name}: {ex.Message}");
                return false;   // la imagen embebida está corrupta: no tiene sentido probar otro tema del mismo álbum con la misma fuente
            }
        }

        return false;
    }

    /// <summary>
    /// Redimensiona (si hace falta) y recodifica SIEMPRE en JPEG baseline:
    /// SkiaSharp nunca produce progresivo con este método, que es
    /// justo el requisito de Rockbox. Escribe a un .tmp y
    /// recién al final renombra, mismo patrón que las descargas por SFTP:
    /// una carátula a medio escribir no puede quedar como si fuera buena.
    /// </summary>
    private static void WriteResizedBaselineJpeg(byte[] embeddedImageBytes, string destPath)
    {
        using var original = SKBitmap.Decode(embeddedImageBytes)
            ?? throw new InvalidDataException("La imagen embebida no se pudo decodificar.");

        var scale = Math.Min(1.0, (double)MaxDimension / Math.Max(original.Width, original.Height));
        SKBitmap final = original;
        SKBitmap? resized = null;
        if (scale < 1.0)
        {
            var targetSize = new SKSizeI(
                Math.Max(1, (int)(original.Width * scale)),
                Math.Max(1, (int)(original.Height * scale)));
            resized = original.Resize(targetSize, SKFilterQuality.Medium)
                ?? throw new InvalidDataException("No se pudo redimensionar la imagen.");
            final = resized;
        }

        try
        {
            using var image = SKImage.FromBitmap(final);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

            var tmp = destPath + SftpService.TempSuffix;
            using (var fs = File.Create(tmp)) data.SaveTo(fs);
            File.Move(tmp, destPath, overwrite: true);
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static string ToLocalPath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);
}
