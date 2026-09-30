using IpodSync.Core.Models;
using IpodSync.Core.Services;

namespace IpodSync.Core.Tests;

/// <summary>
/// El diff y el borrado son la parte peligrosa: un error acá borra música
/// del iPod o recopia 27 GB por SFTP. Se prueban contra una carpeta temporal
/// que hace de raíz del dispositivo, sin servidor y sin iPod.
/// </summary>
public class SyncEngineTests : IDisposable
{
    private readonly string _mount;
    private readonly AppSettings _settings = new()
    {
        Host = "x", Username = "y", RemoteMusicPath = "/musica",
        IpodMusicFolder = "Music",
        IpodPlaylistFolder = "Playlists",
        PlaylistSourceFolder = "Playlist",
        DeleteRemovedFiles = true
    };

    public SyncEngineTests()
    {
        _mount = Path.Combine(Path.GetTempPath(), "ipodsync-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_mount);
    }

    public void Dispose()
    {
        try { Directory.Delete(_mount, recursive: true); } catch { }
    }

    // SftpService no se usa en ComputeDiff/ScanLocal: el diff es puro.
    private SyncEngine Engine() => new(new SftpService(_settings), _settings, _mount);

    /// <summary>Crea un archivo en el iPod. La ruta es relativa a la RAÍZ.</summary>
    private void WriteLocal(string relPath, string content, DateTime? mtimeUtc = null)
    {
        var full = Path.Combine(_mount, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        if (mtimeUtc is { } t) File.SetLastWriteTimeUtc(full, t);
    }

    private static RemoteFile Remote(string rel, long size, DateTime mtimeUtc)
        => new(rel, size, mtimeUtc);

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------- diff basico

    [Fact]
    public void Archivo_solo_en_el_servidor_es_nuevo()
    {
        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 5, T0) });

        Assert.Single(diff.Added);
        Assert.Equal("Music/A/01.flac", diff.Added[0].RelativePath);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Mismo_tamano_y_fecha_es_sin_cambios()
    {
        WriteLocal("Music/A/01.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 5, T0) });

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Updated);
        Assert.Equal(1, diff.UnchangedCount);
        Assert.False(diff.HasChanges);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Diferencia_de_hasta_2s_se_tolera_por_la_resolucion_de_FAT32(int deltaSeconds)
    {
        // Sin esta tolerancia el diff marca TODOS los archivos como
        // modificados y la app recopia la biblioteca entera en cada corrida.
        WriteLocal("Music/A/01.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 5, T0.AddSeconds(deltaSeconds)) });

        Assert.Equal(1, diff.UnchangedCount);
        Assert.Empty(diff.Updated);
    }

    [Fact]
    public void Diferencia_mayor_a_2s_cuenta_como_actualizado()
    {
        WriteLocal("Music/A/01.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 5, T0.AddSeconds(3)) });

        Assert.Single(diff.Updated);
    }

    [Fact]
    public void Distinto_tamano_cuenta_como_actualizado_aunque_coincida_la_fecha()
    {
        WriteLocal("Music/A/01.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 999, T0) });

        Assert.Single(diff.Updated);
    }

    // ------------------------------------------------------- distribucion

    [Fact]
    public void Los_albumes_van_a_Music()
    {
        var diff = Engine().ComputeDiff(new[] { Remote("CD 01/tema.flac", 5, T0) });

        Assert.Equal("Music/CD 01/tema.flac", diff.Added[0].RelativePath);
    }

    [Fact]
    public void Las_pistas_de_una_playlist_van_a_su_carpeta_propia_en_Playlists()
    {
        // Fuera de Music/, para que en el navegador de archivos de Rockbox
        // no se mezclen con los álbumes.
        var diff = Engine().ComputeDiff(new[]
        {
            Remote("Playlist/Auto [71e78f57-e63a-48e5-a66d-75e4569b4cbc]/01.flac", 5, T0)
        });

        Assert.Equal("Playlists/Auto/01.flac", diff.Added[0].RelativePath);
    }

    [Fact]
    public void El_m3u_de_origen_NO_se_copia_al_iPod()
    {
        // Si estuviera en el dispositivo, Rockbox lo mostraría como una
        // playlist más y al abrirla estaría rota: sus rutas relativas fueron
        // escritas para el árbol del servidor. Su contenido va al estado.
        var diff = Engine().ComputeDiff(new[]
        {
            Remote("Playlist/Hip Hop [4041ba25-6d59-44cc-8360-156631ef95aa]/Hip Hop [4041ba25-6d59-44cc-8360-156631ef95aa].m3u", 5, T0)
        });

        Assert.Empty(diff.Added);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Un_m3u_que_quedo_de_una_version_anterior_se_borra_del_iPod()
    {
        // Las versiones viejas sí lo copiaban. Como ya no está en lo que el
        // servidor espera, el borrado normal se lo lleva.
        WriteLocal("Playlists/Auto/Auto [uuid].m3u", "linea", T0);

        var diff = Engine().ComputeDiff(Array.Empty<RemoteFile>());

        Assert.Single(diff.Removed);
    }

    [Fact]
    public void Una_playlist_ya_sincronizada_no_se_vuelve_a_bajar()
    {
        WriteLocal("Playlists/Auto/01.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[]
        {
            Remote("Playlist/Auto [71e78f57-e63a-48e5-a66d-75e4569b4cbc]/01.flac", 5, T0)
        });

        Assert.Equal(1, diff.UnchangedCount);
        Assert.Empty(diff.Removed);
    }

    // ------------------------------------------------------------ borrado

    [Fact]
    public void Archivo_que_ya_no_esta_en_el_servidor_se_marca_para_borrar()
    {
        WriteLocal("Music/A/viejo.flac", "12345", T0);

        var diff = Engine().ComputeDiff(Array.Empty<RemoteFile>());

        Assert.Single(diff.Removed);
        Assert.Equal("Music/A/viejo.flac", diff.Removed[0].RelativePath);
    }

    [Fact]
    public void Con_DeleteRemovedFiles_apagado_no_se_marca_nada_para_borrar()
    {
        _settings.DeleteRemovedFiles = false;
        WriteLocal("Music/A/viejo.flac", "12345", T0);

        Assert.Empty(Engine().ComputeDiff(Array.Empty<RemoteFile>()).Removed);
    }

    [Fact]
    public void database_ignore_nunca_se_borra_porque_vive_solo_en_el_iPod()
    {
        WriteLocal("Playlists/database.ignore", "");

        Assert.Empty(Engine().ComputeDiff(Array.Empty<RemoteFile>()).Removed);
    }

    [Fact]
    public void Los_m3u8_del_catalogo_nunca_se_borran()
    {
        // Los genera esta app y las graba el usuario desde Rockbox: ninguno
        // existe en el servidor, así que el borrado ingenuo se los llevaría
        // puestos en cada sync.
        WriteLocal("Playlists/Auto.m3u8", "#EXTM3U\n");
        WriteLocal("Playlists/Mis Favoritas.m3u8", "#EXTM3U\n");

        Assert.Empty(Engine().ComputeDiff(Array.Empty<RemoteFile>()).Removed);
    }

    [Fact]
    public void Una_pista_suelta_dentro_de_una_playlist_si_se_borra()
    {
        // Solo los .m3u8 de la RAÍZ del catálogo están protegidos; lo que hay
        // dentro de la carpeta de una playlist viene del servidor.
        WriteLocal("Playlists/Auto/viejo.flac", "12345", T0);

        var diff = Engine().ComputeDiff(Array.Empty<RemoteFile>());

        Assert.Single(diff.Removed);
        Assert.Equal("Playlists/Auto/viejo.flac", diff.Removed[0].RelativePath);
    }

    [Fact]
    public void Los_tmp_a_medio_bajar_se_ignoran_en_el_escaneo()
    {
        WriteLocal("Music/A/01.flac" + SftpService.TempSuffix, "parcial");

        var engine = Engine();
        Assert.Empty(engine.ScanLocal());
        Assert.Empty(engine.ComputeDiff(Array.Empty<RemoteFile>()).Removed);
    }

    // ------------------------------------------------------------- nombres

    [Fact]
    public void Nombre_remoto_prohibido_en_FAT32_se_compara_ya_saneado()
    {
        // El archivo en el iPod solo puede llamarse "Song_.flac". Si el diff
        // comparara el nombre crudo del servidor, lo vería faltante siempre.
        WriteLocal("Music/A/Song_.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[] { Remote("A/Song?.flac", 5, T0) });

        Assert.Equal(1, diff.UnchangedCount);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void Mayusculas_distintas_son_el_mismo_archivo_para_FAT32()
    {
        WriteLocal("Music/A/Song.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[] { Remote("a/SONG.FLAC", 5, T0) });

        Assert.Equal(1, diff.UnchangedCount);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void Dos_remotos_que_colisionan_en_FAT32_se_reportan_en_vez_de_pisarse()
    {
        var diff = Engine().ComputeDiff(new[]
        {
            Remote("A/Song.flac", 5, T0),
            Remote("A/SONG.FLAC", 7, T0)
        });

        Assert.Single(diff.Added);
        Assert.Single(diff.Warnings);
        Assert.Contains("Colisión", diff.Warnings[0]);
    }

    // -------------------------------------------------------- transferencia

    [Fact]
    public void Sin_cambios_no_hay_nada_que_transferir()
    {
        WriteLocal("Music/A/01.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 5, T0) });

        Assert.False(diff.HasChanges);
        Assert.Equal(0, diff.BytesToTransfer);
        Assert.Empty(diff.ToTransfer);
    }

    [Fact]
    public void BytesToTransfer_suma_nuevos_y_actualizados()
    {
        WriteLocal("Music/A/02.flac", "12345", T0);

        var diff = Engine().ComputeDiff(new[]
        {
            Remote("A/01.flac", 100, T0),            // nuevo
            Remote("A/02.flac", 200, T0.AddHours(1)) // actualizado
        });

        Assert.Equal(300, diff.BytesToTransfer);
    }

    [Fact]
    public void Un_diff_que_solo_copia_no_necesita_confirmacion()
    {
        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 5, T0) });

        Assert.True(diff.HasChanges);
        Assert.False(diff.RequiresConfirmation);
    }

    [Fact]
    public void Un_diff_que_borra_siempre_necesita_confirmacion()
    {
        // Es lo que frena al auto-sync: si el servidor se monta mal, el diff
        // ve la biblioteca vacia y propone borrar el iPod entero.
        WriteLocal("Music/A/01.flac", "12345", T0);

        var diff = Engine().ComputeDiff(Array.Empty<RemoteFile>());

        Assert.True(diff.RequiresConfirmation);
    }

    [Fact]
    public void Sin_cambios_tampoco_hay_nada_que_confirmar()
    {
        Assert.False(Engine().ComputeDiff(Array.Empty<RemoteFile>()).RequiresConfirmation);
    }

    [Fact]
    public void CanAutoApply_sin_borrados_es_true_sin_importar_el_flag()
    {
        var diff = Engine().ComputeDiff(new[] { Remote("A/01.flac", 5, T0) });

        Assert.True(diff.CanAutoApply(skipDeleteConfirmation: false));
        Assert.True(diff.CanAutoApply(skipDeleteConfirmation: true));
    }

    [Fact]
    public void CanAutoApply_con_borrados_respeta_el_flag_del_usuario()
    {
        // El default (false) es la salida segura: el auto-sync no puede
        // borrar sin que alguien lo haya decidido explícitamente.
        WriteLocal("Music/A/viejo.flac", "12345", T0);
        var diff = Engine().ComputeDiff(Array.Empty<RemoteFile>());

        Assert.False(diff.CanAutoApply(skipDeleteConfirmation: false));
        Assert.True(diff.CanAutoApply(skipDeleteConfirmation: true));
    }

    [Fact]
    public async Task Al_borrar_el_ultimo_tema_se_limpia_la_carpeta_del_album()
    {
        // Rockbox sigue listando las carpetas vacías al navegar: sin esto el
        // usuario ve álbumes fantasma sin una sola canción adentro.
        WriteLocal("Music/Artista/Album/01.flac", "12345", T0);

        var engine = Engine();
        var result = await engine.TransferAsync(engine.ComputeDiff(Array.Empty<RemoteFile>()));

        Assert.Equal(1, result.Deleted);
        Assert.False(Directory.Exists(Path.Combine(_mount, "Music", "Artista")));
        Assert.True(Directory.Exists(Path.Combine(_mount, "Music")),
                    "La raíz de música no se borra nunca.");
    }

    [Fact]
    public async Task Una_ruta_que_se_escapa_de_las_carpetas_administradas_se_bloquea()
    {
        // La letra de unidad del iPod cambia entre sesiones y el dispositivo
        // ya se corrompió dos veces: una ruta mal armada acá borra el disco
        // equivocado. Esta es la última línea de defensa.
        Directory.CreateDirectory(Path.Combine(_mount, "Music"));
        var victima = Path.Combine(_mount, ".rockbox-config.cfg");
        File.WriteAllText(victima, "no me borres");

        var diff = new SyncDiff(
            Array.Empty<SyncItem>(), Array.Empty<SyncItem>(),
            new[] { new SyncItem(SyncAction.Removed, ".rockbox-config.cfg", 0, T0) },
            0, Array.Empty<string>());

        var result = await Engine().TransferAsync(diff);

        Assert.Equal(0, result.Deleted);
        Assert.True(File.Exists(victima), "El guardia dejó borrar fuera de las carpetas administradas.");
        Assert.Contains(result.Errors, e => e.Contains("Bloqueado por seguridad"));
    }
}
