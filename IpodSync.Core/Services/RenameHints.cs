namespace IpodSync.Core.Services;

/// <summary>
/// Diagnóstico, no lógica de sync: separa un diff con muchos "nuevos" y
/// "a borrar" en lo que probablemente es el MISMO archivo bajo otra ruta
/// (por ejemplo, un cambio de convención de nombres o de layout) de lo que
/// es contenido genuinamente distinto.
///
/// Existe porque ese patrón —muchos nuevos y muchos a borrar en la misma
/// corrida, con "sin cambios" bajo— es indistinguible a simple vista de una
/// reorganización sana (como el traslado de playlists de v0.3) o de un choque
/// real entre el layout de esta app y el de una sincronización anterior con
/// otra herramienta. Sin esto, la única forma de saberlo era mirar listas
/// de cientos de rutas a mano.
/// </summary>
public static class RenameHints
{
    public record Candidate(SyncItem Removed, SyncItem Added);

    public record Breakdown(
        IReadOnlyList<Candidate> LikelyRenames,
        IReadOnlyList<SyncItem> GenuinelyNew,
        IReadOnlyList<SyncItem> GenuinelyExtra)
    {
        public int RenameCount => LikelyRenames.Count;
    }

    /// <summary>
    /// Empareja por TAMAÑO EXACTO en bytes. Es una señal fuerte y no una
    /// prueba: dos archivos de audio distintos casi nunca pesan lo mismo al
    /// byte, así que una coincidencia exacta es mucho más probable que sea
    /// el mismo contenido bajo otro nombre o ruta que una casualidad. No usa
    /// hash ni fecha porque para esto —diagnóstico rápido, no el diff en
    /// sí— alcanza y es instantáneo incluso con miles de archivos.
    ///
    /// El emparejamiento es 1 a 1: cada "nuevo" se usa como máximo una vez,
    /// aunque haya varios candidatos del mismo tamaño.
    /// </summary>
    public static Breakdown Analyze(SyncDiff diff)
    {
        var addedBySize = diff.Added
            .Select((item, index) => (item, index))
            .ToLookup(x => x.item.Size);

        var used = new HashSet<int>();
        var renames = new List<Candidate>();

        foreach (var removed in diff.Removed)
        {
            var match = addedBySize[removed.Size].FirstOrDefault(x => !used.Contains(x.index));
            if (match.item is null) continue;

            used.Add(match.index);
            renames.Add(new Candidate(removed, match.item));
        }

        var renamedAdded = renames.Select(r => r.Added).ToHashSet();
        var renamedRemoved = renames.Select(r => r.Removed).ToHashSet();

        return new Breakdown(
            renames,
            diff.Added.Where(a => !renamedAdded.Contains(a)).ToList(),
            diff.Removed.Where(r => !renamedRemoved.Contains(r)).ToList());
    }
}
