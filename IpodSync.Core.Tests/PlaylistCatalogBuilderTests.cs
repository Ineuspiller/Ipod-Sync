using System.Text;
using IpodSync.Core.Models;
using IpodSync.Core.Services;

namespace IpodSync.Core.Tests;

/// <summary>
/// Las playlists son la traducción entre dos convenciones incompatibles:
/// rutas relativas al árbol del servidor vs rutas absolutas desde la raíz del
/// reproductor. Si se equivoca, Rockbox muestra playlists vacías.
/// </summary>
public class PlaylistCatalogBuilderTests : IDisposable
{
    private readonly string _mount;
    private readonly AppSettings _settings = new()
    {
        Host = "x", Username = "y", RemoteMusicPath = "/musica",
        IpodMusicFolder = "Music",
        IpodPlaylistFolder = "Playlists",
        PlaylistSourceFolder = "Playlist"
    };

    public PlaylistCatalogBuilderTests()
    {
        _mount = Path.Combine(Path.GetTempPath(), "ipodsync-pl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_mount, "Playlists"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_mount, recursive: true); } catch { }
    }

    private string At(string rel) =>
        Path.Combine(_mount, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Un tema en el iPod. La ruta es relativa a la RAÍZ.</summary>
    private void Track(string rel)
    {
        var full = At(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "audio");
    }

    private readonly List<PlaylistDefinition> _playlists = new();

    /// <summary>
    /// Una playlist tal como la guardó el sync en el estado del iPod. El .m3u
    /// de origen NO está en el dispositivo a propósito.
    /// </summary>
    private void Playlist(string name, params string[] lines)
    {
        Directory.CreateDirectory(At("Playlists/" + name));
        _playlists.Add(new PlaylistDefinition
        {
            Name = name,
            RemoteFolder = name + " [71e78f57-e63a-48e5-a66d-75e4569b4cbc]",
            Lines = lines.ToList()
        });
    }

    private PlaylistCatalogBuilder Builder() => new(_settings, _mount);

    private PlaylistBuildResult Build() => Builder().Build(_playlists);

    private string[] CatalogEntries(string name) =>
        File.ReadAllText(At("Playlists/" + name + ".m3u8"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToArray();

    // ------------------------------------------------------------------

    [Fact]
    public void Un_single_suelto_queda_en_la_carpeta_de_su_playlist()
    {
        Track("Playlists/Mix/01 tema.flac");
        Playlist("Mix", "01 tema.flac");

        var r = Build();

        Assert.Equal(1, r.PlaylistsWritten);
        Assert.Equal(new[] { "/Playlists/Mix/01 tema.flac" }, CatalogEntries("Mix"));
    }

    [Fact]
    public void Los_dos_puntos_del_dedupe_apuntan_al_album_en_Music()
    {
        // Es lo que hace tidekeeper_dedupe.py: reescribe la entrada para que
        // apunte al álbum completo en vez de dejar el single duplicado.
        // El `../../` cuenta saltos en el árbol del SERVIDOR, no en el iPod.
        Track("Music/CD 01/Daft Punk - Give Life Back to Music.flac");
        Playlist("daft punk", "../../CD 01/Daft Punk - Give Life Back to Music.flac");

        var r = Build();

        Assert.Equal(new[] { "/Music/CD 01/Daft Punk - Give Life Back to Music.flac" },
                     CatalogEntries("daft punk"));
        Assert.Equal(0, r.EntriesDropped);
    }

    [Fact]
    public void Una_playlist_mezcla_singles_propios_con_temas_de_albumes()
    {
        Track("Playlists/Mix/02 suelto.flac");
        Track("Music/Album/01 del album.flac");
        Playlist("Mix", "../../Album/01 del album.flac", "02 suelto.flac");

        Build();

        Assert.Equal(new[]
        {
            "/Music/Album/01 del album.flac",
            "/Playlists/Mix/02 suelto.flac"
        }, CatalogEntries("Mix"));
    }

    [Fact]
    public void El_m3u8_se_llama_como_la_carpeta_sin_uuid()
    {
        Track("Playlists/daft punk/01.flac");
        Playlist("daft punk", "01.flac");

        Build();

        Assert.True(File.Exists(At("Playlists/daft punk.m3u8")));
    }

    [Fact]
    public void Una_entrada_que_no_llego_al_iPod_se_descarta()
    {
        Track("Playlists/Mix/01 existe.flac");
        Playlist("Mix", "01 existe.flac", "02 no bajo.flac");

        var r = Build();

        Assert.Equal(1, r.EntriesWritten);
        Assert.Equal(1, r.EntriesDropped);
        Assert.Single(CatalogEntries("Mix"));
    }

    [Fact]
    public void Una_linea_que_se_escapa_de_la_biblioteca_se_descarta_y_avisa()
    {
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "01.flac", "../../../../etc/passwd");

        var r = Build();

        Assert.Equal(1, r.EntriesDropped);
        Assert.Contains(r.Warnings, w => w.Contains("fuera de la biblioteca"));
        Assert.Single(CatalogEntries("Mix"));
    }

    [Fact]
    public void Los_comentarios_EXTINF_se_ignoran()
    {
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "#EXTM3U", "#EXTINF:123,Artista - Tema", "01.flac", "");

        Build();

        Assert.Single(CatalogEntries("Mix"));
    }

    [Fact]
    public void El_m3u8_se_escribe_en_UTF8_sin_BOM()
    {
        // Rockbox no digiere el BOM: la primera entrada sale rota.
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "01.flac");

        Build();

        var bytes = File.ReadAllBytes(At("Playlists/Mix.m3u8"));
        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void Se_respeta_el_Unicode_de_los_nombres_reales()
    {
        Track("Music/虹伝説2 - THE WHITE GOBLIN/01 Neuköln.flac");
        Playlist("Mix", "../../虹伝説2 - THE WHITE GOBLIN/01 Neuköln.flac");

        Build();

        Assert.Equal(new[] { "/Music/虹伝説2 - THE WHITE GOBLIN/01 Neuköln.flac" },
                     CatalogEntries("Mix"));
    }

    [Fact]
    public void Un_nombre_saneado_por_FAT32_igual_se_encuentra()
    {
        // El .m3u trae el nombre del servidor ("Song?.flac"), pero en el iPod
        // el archivo quedó como "Song_.flac". Sin buscar por la misma clave
        // que usó el motor de sync, la entrada se descartaría siempre.
        Track("Music/Album/Song_.flac");
        Playlist("Mix", "../../Album/Song?.flac");

        var r = Build();

        Assert.Equal(0, r.EntriesDropped);
        Assert.Equal(new[] { "/Music/Album/Song_.flac" }, CatalogEntries("Mix"));
    }

    [Fact]
    public void Se_crea_database_ignore_en_la_raiz_del_catalogo()
    {
        // Sin esto las pistas sueltas de las playlists aparecen como álbumes
        // falsos en el browser por artista/álbum.
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "01.flac");

        var r = Build();

        Assert.True(File.Exists(At("Playlists/database.ignore")));
        Assert.Contains(r.Warnings, w => w.Contains("Initialize now"));
    }

    [Fact]
    public void Una_playlist_sin_entradas_validas_no_se_escribe()
    {
        Playlist("Mix", "no-existe.flac");

        var r = Build();

        Assert.Equal(0, r.PlaylistsWritten);
        Assert.False(File.Exists(At("Playlists/Mix.m3u8")));
        Assert.Contains(r.Warnings, w => w.Contains("vacía"));
    }

    [Fact]
    public void Un_m3u8_generado_antes_y_ya_sin_origen_se_limpia()
    {
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "01.flac");
        Build();

        // La playlist desaparece del servidor: su definición ya no viene.
        _playlists.Clear();
        Directory.Delete(At("Playlists/Mix"), recursive: true);
        Track("Playlists/Otra/01.flac");
        Playlist("Otra", "01.flac");

        var r = Build();

        Assert.Equal(1, r.StaleRemoved);
        Assert.False(File.Exists(At("Playlists/Mix.m3u8")));
        Assert.True(File.Exists(At("Playlists/Otra.m3u8")));
    }

    [Fact]
    public void Una_playlist_hecha_a_mano_en_Rockbox_no_se_toca()
    {
        // Rockbox graba sus propias playlists en la misma carpeta. Borrar
        // todo lo que no generamos nosotros se las llevaría puestas.
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "01.flac");

        var mia = At("Playlists/Mis favoritas.m3u8");
        File.WriteAllText(mia, "/Music/algo.flac\n");

        Build();

        Assert.True(File.Exists(mia), "Se borró una playlist del usuario.");
    }

    [Fact]
    public void El_m3u_de_origen_no_queda_en_el_iPod()
    {
        // La experiencia en el reproductor tiene que ser: álbumes en Music,
        // playlists en Playlists, y nada más que mirar.
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "01.flac");

        Build();

        Assert.Empty(Directory.EnumerateFiles(_mount, "*.m3u", SearchOption.AllDirectories));
    }

    [Fact]
    public void El_catalogo_se_rearma_desde_el_estado_sin_conexion()
    {
        Track("Playlists/Mix/01.flac");
        Playlist("Mix", "01.flac");

        SyncStateService.Save(_mount, new SyncState { Playlists = _playlists });

        // Build() sin argumentos lee el estado guardado en la raíz del iPod.
        var r = Builder().Build();

        Assert.Equal(1, r.PlaylistsWritten);
        Assert.Equal(new[] { "/Playlists/Mix/01.flac" }, CatalogEntries("Mix"));
    }

    [Fact]
    public void Sin_carpeta_de_catalogo_no_explota()
    {
        Directory.Delete(At("Playlists"), recursive: true);

        var r = Build();

        Assert.Equal(0, r.PlaylistsWritten);
        Assert.Single(r.Warnings);
    }
}
