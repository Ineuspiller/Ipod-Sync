using IpodSync.Core.Models;
using IpodSync.Core.Services;
using SkiaSharp;

namespace IpodSync.Core.Tests;

/// <summary>
/// CoverExtractor completa las carátulas que Rockbox no puede leer (las
/// embebidas) extrayéndolas a un cover.jpg suelto, igual que hace
/// extract_covers.py a mano en el servidor — pero acá sobre lo que YA está
/// en el iPod, sin tocar SFTP. Los .flac de Fixtures/ son reales (generados
/// con ffmpeg), no texto disfrazado: TagLib necesita parsear el contenedor
/// de verdad para encontrar la imagen.
/// </summary>
public class CoverExtractorTests : IDisposable
{
    private readonly string _mount;
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly AppSettings _settings = new() { IpodMusicFolder = "Music" };

    public CoverExtractorTests()
    {
        _mount = Path.Combine(Path.GetTempPath(), "ipodsync-cover-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_mount);
    }

    public void Dispose()
    {
        try { Directory.Delete(_mount, recursive: true); } catch { }
    }

    private string MusicRoot => Path.Combine(_mount, "Music");

    private void PutTrack(string albumDir, string trackFileName, string fixtureName)
    {
        var dir = Path.Combine(MusicRoot, albumDir);
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(FixturesDir, fixtureName), Path.Combine(dir, trackFileName));
    }

    private List<AlbumEntry> Scan() => new LibraryBrowser().ScanAlbums(_settings, _mount);

    [Fact]
    public void Extrae_la_caratula_embebida_a_un_cover_jpg_suelto()
    {
        PutTrack("Album Uno", "01.flac", "con-caratula.flac");

        var result = new CoverExtractor().FixMissingCovers(Scan(), MusicRoot);

        Assert.Equal(1, result.Extracted);
        Assert.Equal(0, result.Skipped);
        var coverPath = Path.Combine(MusicRoot, "Album Uno", "cover.jpg");
        Assert.True(File.Exists(coverPath));
    }

    [Fact]
    public void La_caratula_escrita_es_una_imagen_valida()
    {
        PutTrack("Album Uno", "01.flac", "con-caratula.flac");
        new CoverExtractor().FixMissingCovers(Scan(), MusicRoot);

        var coverPath = Path.Combine(MusicRoot, "Album Uno", "cover.jpg");
        using var bmp = SKBitmap.Decode(coverPath);

        Assert.NotNull(bmp);
        Assert.True(bmp!.Width > 0 && bmp.Height > 0);
        // La fuente es 400x400: sin necesidad de redimensionar, no debería
        // pasarse del límite de todos modos.
        Assert.True(bmp.Width <= 500 && bmp.Height <= 500);
    }

    [Fact]
    public void Un_album_sin_ninguna_caratula_embebida_queda_como_omitido()
    {
        PutTrack("Sin Caratula", "01.flac", "sin-caratula.flac");

        var result = new CoverExtractor().FixMissingCovers(Scan(), MusicRoot);

        Assert.Equal(0, result.Extracted);
        Assert.Equal(1, result.Skipped);
        Assert.False(File.Exists(Path.Combine(MusicRoot, "Sin Caratula", "cover.jpg")));
    }

    [Fact]
    public void Un_album_que_ya_tiene_caratula_no_se_toca()
    {
        PutTrack("Album Uno", "01.flac", "con-caratula.flac");
        var existing = Path.Combine(MusicRoot, "Album Uno", "cover.jpg");
        File.WriteAllText(existing, "ya-tenia-una-antes");
        var before = File.GetLastWriteTimeUtc(existing);

        var result = new CoverExtractor().FixMissingCovers(Scan(), MusicRoot);

        Assert.Equal(0, result.Extracted);
        Assert.Equal(0, result.Skipped);   // ni siquiera cuenta: no había nada que resolver
        Assert.Equal("ya-tenia-una-antes", File.ReadAllText(existing));
        Assert.Equal(before, File.GetLastWriteTimeUtc(existing));
    }

    [Fact]
    public void Prueba_con_el_siguiente_tema_si_el_primero_no_trae_caratula()
    {
        PutTrack("Album Mixto", "01 sin.flac", "sin-caratula.flac");
        PutTrack("Album Mixto", "02 con.flac", "con-caratula.flac");

        var result = new CoverExtractor().FixMissingCovers(Scan(), MusicRoot);

        Assert.Equal(1, result.Extracted);
        Assert.True(File.Exists(Path.Combine(MusicRoot, "Album Mixto", "cover.jpg")));
    }

    [Fact]
    public void La_caratula_extraida_sobrevive_al_borrado_de_la_sync_siguiente()
    {
        // EL bug: la carátula la extraemos NOSOTROS, así que no existe en el
        // servidor. El borrado de la sync siguiente ve un archivo que el
        // servidor no tiene y se lo lleva — y la sync de después la vuelve a
        // extraer. Escrituras de más para siempre sobre la SD del iPod.
        PutTrack("Album Uno", "01.flac", "con-caratula.flac");
        new CoverExtractor().FixMissingCovers(Scan(), MusicRoot);

        var coverPath = Path.Combine(MusicRoot, "Album Uno", "cover.jpg");
        Assert.True(File.Exists(coverPath), "No se extrajo la carátula, el test no prueba nada.");

        // El servidor tiene el tema pero NO la carátula (por eso hubo que
        // extraerla), y el borrado está activo: el caso exacto del bug.
        var settings = new AppSettings
        {
            IpodMusicFolder = "Music",
            IpodPlaylistFolder = "Playlists",
            PlaylistSourceFolder = "Playlist",
            DeleteRemovedFiles = true
        };
        var engine = new SyncEngine(new SftpService(settings), settings, _mount);
        var remote = new[] { new RemoteFile("Album Uno/01.flac", 1, DateTime.UtcNow) };

        var diff = engine.ComputeDiff(remote);

        Assert.DoesNotContain(diff.Removed, r => r.RelativePath.EndsWith("cover.jpg"));
    }

    [Fact]
    public void Cuenta_extraidos_y_omitidos_por_separado_en_un_lote_mixto()
    {
        PutTrack("Con Caratula", "01.flac", "con-caratula.flac");
        PutTrack("Sin Caratula A", "01.flac", "sin-caratula.flac");
        PutTrack("Sin Caratula B", "01.flac", "sin-caratula.flac");

        var result = new CoverExtractor().FixMissingCovers(Scan(), MusicRoot);

        Assert.Equal(1, result.Extracted);
        Assert.Equal(2, result.Skipped);
    }
}
