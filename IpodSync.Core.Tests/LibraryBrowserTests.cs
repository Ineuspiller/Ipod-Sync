using IpodSync.Core.Models;
using IpodSync.Core.Services;

namespace IpodSync.Core.Tests;

/// <summary>
/// El browser de biblioteca es de solo lectura sobre lo que YA está en el
/// iPod: nada de SFTP acá, solo el
/// agrupamiento por carpeta y la detección de carátula.
/// </summary>
public class LibraryBrowserTests : IDisposable
{
    private readonly string _mount;
    private readonly AppSettings _settings = new()
    {
        Host = "x", Username = "y", RemoteMusicPath = "/musica",
        IpodMusicFolder = "Music",
        IpodPlaylistFolder = "Playlists",
        PlaylistSourceFolder = "Playlist"
    };

    public LibraryBrowserTests()
    {
        _mount = Path.Combine(Path.GetTempPath(), "ipodsync-lib-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_mount);
    }

    public void Dispose()
    {
        try { Directory.Delete(_mount, recursive: true); } catch { }
    }

    private void WriteFile(string relPath, string content = "x")
    {
        var full = Path.Combine(_mount, "Music", relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Una_carpeta_con_audio_es_un_album()
    {
        WriteFile("Radiohead - OK Computer/01 Airbag.flac");
        WriteFile("Radiohead - OK Computer/02 Paranoid Android.flac");

        var albums = new LibraryBrowser().ScanAlbums(_settings, _mount);

        var album = Assert.Single(albums);
        Assert.Equal("Radiohead - OK Computer", album.Name);
        Assert.Equal(2, album.Tracks.Count);
        Assert.Equal("01 Airbag", album.Tracks[0].Title);
    }

    [Fact]
    public void Una_carpeta_sin_audio_directo_no_es_un_album()
    {
        // Es una carpeta contenedora (ej. si algún día se agrupara por
        // artista): no tiene sentido mostrarla como si fuera un álbum vacío.
        WriteFile("Radiohead/OK Computer/01 Airbag.flac");

        var albums = new LibraryBrowser().ScanAlbums(_settings, _mount);

        var album = Assert.Single(albums);
        Assert.Equal("OK Computer", album.Name);
    }

    [Fact]
    public void Encuentra_la_caratula_suelta_en_la_carpeta()
    {
        WriteFile("Heroes/01 Heroes.flac");
        WriteFile("Heroes/cover.jpg", "fake-jpg-bytes");

        var albums = new LibraryBrowser().ScanAlbums(_settings, _mount);

        var album = Assert.Single(albums);
        Assert.NotNull(album.CoverRelativePath);
        Assert.EndsWith("cover.jpg", album.CoverRelativePath);
    }

    [Fact]
    public void Sin_caratula_suelta_CoverRelativePath_es_null()
    {
        WriteFile("Sin Portada/01.flac");

        var albums = new LibraryBrowser().ScanAlbums(_settings, _mount);

        Assert.Null(Assert.Single(albums).CoverRelativePath);
    }

    [Fact]
    public void Archivos_que_no_son_audio_ni_caratula_se_ignoran()
    {
        WriteFile("Album/01.flac");
        WriteFile("Album/notas.txt");
        WriteFile("Album/playlist.m3u.bak");

        var albums = new LibraryBrowser().ScanAlbums(_settings, _mount);

        Assert.Single(Assert.Single(albums).Tracks);
    }

    [Fact]
    public void Los_albumes_salen_ordenados_por_nombre()
    {
        WriteFile("Zeta/01.flac");
        WriteFile("Alfa/01.flac");
        WriteFile("Medio/01.flac");

        var albums = new LibraryBrowser().ScanAlbums(_settings, _mount);

        Assert.Equal(new[] { "Alfa", "Medio", "Zeta" }, albums.Select(a => a.Name));
    }

    [Fact]
    public void Sin_carpeta_Music_devuelve_lista_vacia()
    {
        // El iPod recién detectado, antes de la primera sync, no tiene
        // Music/ todavía: no debe explotar, solo no mostrar nada.
        var albums = new LibraryBrowser().ScanAlbums(_settings, _mount);

        Assert.Empty(albums);
    }

    [Fact]
    public void Las_pistas_salen_ordenadas_por_titulo()
    {
        WriteFile("Album/03 Tercera.flac");
        WriteFile("Album/01 Primera.flac");
        WriteFile("Album/02 Segunda.flac");

        var album = Assert.Single(new LibraryBrowser().ScanAlbums(_settings, _mount));

        Assert.Equal(new[] { "01 Primera", "02 Segunda", "03 Tercera" },
                     album.Tracks.Select(t => t.Title));
    }
}
