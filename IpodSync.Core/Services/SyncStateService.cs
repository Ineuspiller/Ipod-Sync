using System.Text;
using System.Text.Json;
using IpodSync.Core.Models;

namespace IpodSync.Core.Services;

/// <summary>Lee y escribe el estado en la raíz del iPod.</summary>
public static class SyncStateService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string PathFor(string mountRoot) =>
        Path.Combine(mountRoot, SyncState.FileName);

    public static SyncState Load(string mountRoot)
    {
        var path = PathFor(mountRoot);
        if (!File.Exists(path)) return new SyncState();

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path),
                                              AppJsonContext.Default.SyncState) ?? new SyncState();
        }
        catch
        {
            // Estado corrupto: se arranca limpio en vez de explotar. Lo peor
            // que pasa es que el catálogo de playlists quede vacío hasta la
            // próxima sync con conexión.
            return new SyncState();
        }
    }

    public static void Save(string mountRoot, SyncState state)
    {
        var path = PathFor(mountRoot);
        var tmp = path + SftpService.TempSuffix;

        var json = JsonSerializer.Serialize(state, AppJsonContext.Relaxed.SyncState);
        File.WriteAllText(tmp, json, Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }
}
