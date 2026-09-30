using IpodSync.Core.Models;
using IpodSync.Core.Services;

namespace IpodSync.Core.Tests;

/// <summary>
/// Que lo guardado se pueda volver a leer, completo. Parece obvio y no tenía
/// ni un test: es justo el camino que el trimmer del publish rompe en
/// silencio si la serialización va por reflexión (ver AppJsonContext). Un
/// settings.json que se guarda vacío se traduce en la app olvidándose del
/// servidor configurado en cada arranque, sin ningún error visible.
/// </summary>
public class PersistenceTests : IDisposable
{
    private readonly string _dir;

    public PersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ipodsync-persist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Los_settings_sobreviven_una_vuelta_completa_por_disco()
    {
        var path = Path.Combine(_dir, "settings.json");
        var original = new AppSettings
        {
            Host = "192.168.1.50",
            Port = 2222,
            Username = "testuser",
            AuthMode = AuthMode.PrivateKey,
            PrivateKeyPath = "/home/testuser/.ssh/id_ed25519",
            EncryptedPassword = "cifrado-base64",
            EncryptedKeyPassphrase = "otro-cifrado",
            RemoteMusicPath = "/srv/musica",
            IpodMusicFolder = "Music",
            IpodPlaylistFolder = "Playlists",
            PlaylistSourceFolder = "Playlist",
            Language = AppLanguage.English,
            AutoSyncOnConnect = false,
            DeleteRemovedFiles = false,
            BuildPlaylistCatalog = false,
            StartMinimized = true,
            AutoSyncSkipDeleteConfirmation = true,
            ExcludePatterns = new List<string> { "*.nfo", "propio.txt" }
        };

        new SettingsService(path).Save(original);
        var loaded = new SettingsService(path).Load();

        Assert.Equal(original.Host, loaded.Host);
        Assert.Equal(original.Port, loaded.Port);
        Assert.Equal(original.Username, loaded.Username);
        Assert.Equal(original.AuthMode, loaded.AuthMode);
        Assert.Equal(original.PrivateKeyPath, loaded.PrivateKeyPath);
        Assert.Equal(original.EncryptedPassword, loaded.EncryptedPassword);
        Assert.Equal(original.EncryptedKeyPassphrase, loaded.EncryptedKeyPassphrase);
        Assert.Equal(original.RemoteMusicPath, loaded.RemoteMusicPath);
        Assert.Equal(original.IpodMusicFolder, loaded.IpodMusicFolder);
        Assert.Equal(original.IpodPlaylistFolder, loaded.IpodPlaylistFolder);
        Assert.Equal(original.PlaylistSourceFolder, loaded.PlaylistSourceFolder);
        Assert.Equal(original.Language, loaded.Language);
        Assert.Equal(original.AutoSyncOnConnect, loaded.AutoSyncOnConnect);
        Assert.Equal(original.DeleteRemovedFiles, loaded.DeleteRemovedFiles);
        Assert.Equal(original.BuildPlaylistCatalog, loaded.BuildPlaylistCatalog);
        Assert.Equal(original.StartMinimized, loaded.StartMinimized);
        Assert.Equal(original.AutoSyncSkipDeleteConfirmation, loaded.AutoSyncSkipDeleteConfirmation);
        Assert.Equal(original.ExcludePatterns, loaded.ExcludePatterns);
    }

    [Fact]
    public void Sin_archivo_los_settings_arrancan_en_default_sin_explotar()
    {
        var loaded = new SettingsService(Path.Combine(_dir, "no-existe.json")).Load();

        Assert.False(loaded.IsConfigured);
        Assert.Equal(22, loaded.Port);
    }

    [Fact]
    public void Un_settings_corrupto_no_tira_la_app()
    {
        var path = Path.Combine(_dir, "roto.json");
        File.WriteAllText(path, "{ esto no es json valido ");

        var loaded = new SettingsService(path).Load();

        Assert.False(loaded.IsConfigured);
    }

    [Fact]
    public void El_estado_del_ipod_sobrevive_una_vuelta_completa_por_disco()
    {
        var original = new SyncState
        {
            LastSyncUtc = new DateTime(2026, 9, 24, 3, 14, 15, DateTimeKind.Utc),
            SourceHost = "testuser@192.168.1.50",
            RemoteMusicPath = "/srv/musica",
            TrackCount = 1068,
            TotalBytes = 28_900_000_000L,
            LastDiffSummary = "0 nuevos, 0 actualizados",
            Playlists = new List<PlaylistDefinition>
            {
                new() { Name = "Auto", RemoteFolder = "Auto [uuid]", Lines = new List<string> { "../CD 01/x.flac" } }
            }
        };

        SyncStateService.Save(_dir, original);
        var loaded = SyncStateService.Load(_dir);

        Assert.Equal(original.LastSyncUtc, loaded.LastSyncUtc);
        Assert.Equal(original.SourceHost, loaded.SourceHost);
        Assert.Equal(original.RemoteMusicPath, loaded.RemoteMusicPath);
        Assert.Equal(original.TrackCount, loaded.TrackCount);
        Assert.Equal(original.TotalBytes, loaded.TotalBytes);
        Assert.Equal(original.LastDiffSummary, loaded.LastDiffSummary);
        var playlist = Assert.Single(loaded.Playlists);
        Assert.Equal("Auto", playlist.Name);
        Assert.Equal(new[] { "../CD 01/x.flac" }, playlist.Lines);
    }

    [Fact]
    public void Los_nombres_en_japones_quedan_legibles_en_el_estado()
    {
        // La biblioteca real tiene japonés, cirílico y comillas tipográficas.
        // Sin UnsafeRelaxedJsonEscaping el archivo queda ilegible, lleno de
        // \uXXXX — y este archivo se abre a mano cuando algo anda mal.
        var state = new SyncState
        {
            Playlists = new List<PlaylistDefinition>
            {
                new() { Name = "虹伝説2 — THE WHITE GOBLIN", Lines = new List<string> { "Neuköln.flac" } }
            }
        };

        SyncStateService.Save(_dir, state);
        var raw = File.ReadAllText(SyncStateService.PathFor(_dir));

        Assert.Contains("虹伝説2", raw);
        Assert.Contains("Neuköln", raw);
        Assert.DoesNotContain("\\u", raw);

        Assert.Equal("虹伝説2 — THE WHITE GOBLIN", Assert.Single(SyncStateService.Load(_dir).Playlists).Name);
    }
}
