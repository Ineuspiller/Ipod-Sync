using IpodSync.Core.Services;

namespace IpodSync.Core.Tests;

public class RenameHintsTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SyncItem Item(SyncAction action, string path, long size) =>
        new(action, path, size, T0);

    private static SyncDiff Diff(IEnumerable<SyncItem> added, IEnumerable<SyncItem> removed) =>
        new(added.ToList(), Array.Empty<SyncItem>(), removed.ToList(), 0, Array.Empty<string>());

    [Fact]
    public void Mismo_tamano_exacto_se_marca_como_posible_renombre()
    {
        var diff = Diff(
            added:   new[] { Item(SyncAction.Added, "Playlists/Mix/01.flac", 5_000_000) },
            removed: new[] { Item(SyncAction.Removed, "Music/Playlist/Mix [uuid]/01.flac", 5_000_000) });

        var result = RenameHints.Analyze(diff);

        Assert.Equal(1, result.RenameCount);
        Assert.Empty(result.GenuinelyNew);
        Assert.Empty(result.GenuinelyExtra);
    }

    [Fact]
    public void Tamano_distinto_no_se_empareja()
    {
        var diff = Diff(
            added:   new[] { Item(SyncAction.Added, "A/nuevo.flac", 4_000_000) },
            removed: new[] { Item(SyncAction.Removed, "A/viejo.flac", 3_999_999) });

        var result = RenameHints.Analyze(diff);

        Assert.Empty(result.LikelyRenames);
        Assert.Single(result.GenuinelyNew);
        Assert.Single(result.GenuinelyExtra);
    }

    [Fact]
    public void El_emparejamiento_es_uno_a_uno_aunque_coincidan_varios_tamanos()
    {
        // Tres archivos "nuevos" del mismo tamaño y dos "a borrar" del mismo
        // tamaño: como mucho dos emparejamientos, nunca que el mismo
        // "nuevo" se cuente dos veces como renombre.
        var diff = Diff(
            added: new[]
            {
                Item(SyncAction.Added, "A/1.flac", 1000),
                Item(SyncAction.Added, "A/2.flac", 1000),
                Item(SyncAction.Added, "A/3.flac", 1000),
            },
            removed: new[]
            {
                Item(SyncAction.Removed, "B/1.flac", 1000),
                Item(SyncAction.Removed, "B/2.flac", 1000),
            });

        var result = RenameHints.Analyze(diff);

        Assert.Equal(2, result.RenameCount);
        Assert.Single(result.GenuinelyNew);
        Assert.Empty(result.GenuinelyExtra);

        // Cada "added" aparece emparejado a lo sumo una vez.
        var pairedAdded = result.LikelyRenames.Select(r => r.Added).ToList();
        Assert.Equal(pairedAdded.Count, pairedAdded.Distinct().Count());
    }

    [Fact]
    public void Sin_removidos_todo_es_genuinamente_nuevo()
    {
        var diff = Diff(
            added:   new[] { Item(SyncAction.Added, "A/1.flac", 1000) },
            removed: Array.Empty<SyncItem>());

        var result = RenameHints.Analyze(diff);

        Assert.Empty(result.LikelyRenames);
        Assert.Single(result.GenuinelyNew);
    }

    [Fact]
    public void Sin_agregados_todo_sobra_de_verdad()
    {
        var diff = Diff(
            added:   Array.Empty<SyncItem>(),
            removed: new[] { Item(SyncAction.Removed, "A/1.flac", 1000) });

        var result = RenameHints.Analyze(diff);

        Assert.Empty(result.LikelyRenames);
        Assert.Single(result.GenuinelyExtra);
    }

    [Fact]
    public void Diff_vacio_no_explota()
    {
        var result = RenameHints.Analyze(Diff(Array.Empty<SyncItem>(), Array.Empty<SyncItem>()));

        Assert.Empty(result.LikelyRenames);
        Assert.Empty(result.GenuinelyNew);
        Assert.Empty(result.GenuinelyExtra);
    }
}
