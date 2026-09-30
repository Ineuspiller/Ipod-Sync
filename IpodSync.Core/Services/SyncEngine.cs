using System.Collections.Concurrent;
using IpodSync.Core.Devices;
using IpodSync.Core.Models;

namespace IpodSync.Core.Services;

public enum SyncAction
{
    /// <summary>Está en el servidor y no en el iPod.</summary>
    Added,
    /// <summary>Está en los dos pero cambió el tamaño o la fecha.</summary>
    Updated,
    /// <summary>Está en el iPod y ya no en el servidor.</summary>
    Removed
}

/// <summary><see cref="RelativePath"/> es relativo a la RAÍZ del iPod, no a
/// la carpeta de música: un archivo puede terminar en Music/ o en Playlists/
/// según lo que decida <see cref="IpodLayout"/>.</summary>
public record SyncItem(SyncAction Action, string RelativePath, long Size, DateTime RemoteLastWriteUtc)
{
    /// <summary>El de origen, sin sanear. Sirve para volver a pedirlo por SFTP.</summary>
    public string SourcePath { get; init; } = "";
}

/// <summary>Resultado del pre-check. Barato: no toca ningún archivo.</summary>
public record SyncDiff(
    IReadOnlyList<SyncItem> Added,
    IReadOnlyList<SyncItem> Updated,
    IReadOnlyList<SyncItem> Removed,
    int UnchangedCount,
    IReadOnlyList<string> Warnings)
{
    public bool HasChanges => Added.Count > 0 || Updated.Count > 0 || Removed.Count > 0;

    /// <summary>
    /// Este diff no se puede aplicar sin que una persona diga que sí.
    ///
    /// Copiar de más es molesto; borrar de más es perder música. Y el caso
    /// que importa no es el usuario distraído: si el servidor se monta mal o
    /// la ruta remota apunta a una carpeta vacía, el diff ve una biblioteca
    /// sin nada y propone borrar el iPod entero. El auto-sync al conectar
    /// copia solo; para borrar siempre pregunta.
    /// </summary>
    public bool RequiresConfirmation => Removed.Count > 0;

    /// <summary>
    /// Si el auto-sync al conectar el iPod puede transferir este diff sin
    /// que nadie lo mire. Separado de RequiresConfirmation porque el
    /// usuario puede optar explícitamente por saltarse también la
    /// confirmación de borrado (AppSettings.AutoSyncSkipDeleteConfirmation)
    /// — una decisión suya, no el default.
    /// </summary>
    public bool CanAutoApply(bool skipDeleteConfirmation) =>
        !RequiresConfirmation || skipDeleteConfirmation;

    public IEnumerable<SyncItem> ToTransfer => Added.Concat(Updated);

    public long BytesToTransfer => ToTransfer.Sum(i => i.Size);

    public string Summary =>
        $"{Added.Count} nuevos, {Updated.Count} actualizados, " +
        $"{Removed.Count} a borrar, {UnchangedCount} sin cambios " +
        $"({BytesToTransfer / 1024d / 1024d:F0} MB a transferir)";
}

public record SyncProgress(
    int FileIndex,
    int TotalFiles,
    string CurrentFile,
    long BytesDone,
    long BytesTotal,
    TimeSpan Elapsed)
{
    public double Percent => BytesTotal <= 0 ? 100 : Math.Min(100, BytesDone * 100.0 / BytesTotal);

    /// <summary>Estimación por tasa promedio. Null hasta tener muestra suficiente.</summary>
    public TimeSpan? Eta
    {
        get
        {
            if (BytesDone <= 0 || Elapsed.TotalSeconds < 2) return null;
            var rate = BytesDone / Elapsed.TotalSeconds;
            var left = BytesTotal - BytesDone;
            return left <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(left / rate);
        }
    }
}

public record SyncResult(
    int Transferred,
    int Deleted,
    int Failed,
    long BytesTransferred,
    TimeSpan Duration,
    IReadOnlyList<string> Errors);

/// <summary>
/// El motor de sincronización: reemplaza lo que hacía rsync.
///
///     ListAllFiles(remoto) -> ScanLocal(iPod) -> ComputeDiff -> TransferAsync
///
/// El diff está deliberadamente separado de la transferencia: calcularlo es
/// barato y no toca el iPod, así que la app puede reportar "no hay nada que
/// hacer" en segundos. Importa porque el usuario enchufa el iPod para
/// escuchar música, no siempre para sincronizar.
/// </summary>
public class SyncEngine
{
    /// <summary>
    /// FAT32 guarda las fechas con 2 segundos de resolución. Sin esta
    /// tolerancia el diff marca TODOS los archivos como modificados y
    /// recopia la biblioteca entera en cada corrida. Es el equivalente
    /// a --modify-window=2 de rsync.
    /// </summary>
    public const int FatTimeToleranceSeconds = 2;

    /// <summary>
    /// Cuántos archivos se descargan al mismo tiempo. El motor abre esa
    /// cantidad de conexiones SFTP independientes (nunca comparte una entre
    /// threads: SSH.NET no lo garantiza como seguro). El límite real termina
    /// siendo la escritura al iPod, no la red, así que un número alto no
    /// ayuda y con el adaptador de SD de por medio conviene no forzarlo:
    /// 3 es un punto medio entre velocidad real y no saturar la escritura.
    /// </summary>
    private const int MaxParallelTransfers = 3;

    /// <summary>Cada cuánto, como mucho, se reporta progreso mientras un
    /// archivo baja. La UI redibuja a 10 Hz: 50 ms le da el doble de
    /// muestras de las que puede mostrar.</summary>
    private const int ProgressIntervalMs = 50;

    private readonly SftpService _sftp;
    private readonly AppSettings _settings;
    private readonly IpodLayout _layout;

    public SyncEngine(SftpService sftp, AppSettings settings, IpodDevice device)
        : this(sftp, settings, device.MountPath) { }

    /// <summary>La raíz es la del DISPOSITIVO. Una carpeta común sirve para
    /// probar sin un iPod de por medio.</summary>
    public SyncEngine(SftpService sftp, AppSettings settings, string mountRoot)
        : this(sftp, settings, new IpodLayout(settings, mountRoot)) { }

    public SyncEngine(SftpService sftp, AppSettings settings, IpodLayout layout)
    {
        _sftp = sftp;
        _settings = settings;
        _layout = layout;
    }

    public IpodLayout Layout => _layout;

    // ---------------------------------------------------------------- scan

    /// <summary><see cref="RelativePath"/> es relativo a la raíz del iPod.</summary>
    public record LocalFile(string RelativePath, long Size, DateTime LastWriteUtc);

    /// <summary>
    /// Recorre las carpetas del iPod que el sync administra. Los .tmp de una
    /// corrida interrumpida se ignoran: no son ni archivos válidos ni
    /// archivos de más para borrar.
    /// </summary>
    public List<LocalFile> ScanLocal(CancellationToken ct = default)
    {
        var result = new List<LocalFile>();

        foreach (var root in _layout.WritableRoots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (path.EndsWith(SftpService.TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                FileInfo fi;
                try { fi = new FileInfo(path); }
                catch { continue; }   // se desconectó a mitad del escaneo

                var rel = Path.GetRelativePath(_layout.MountRoot, path).Replace('\\', '/');
                result.Add(new LocalFile(rel, fi.Length, fi.LastWriteTimeUtc));
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- diff

    public SyncDiff ComputeDiff(
        IReadOnlyList<RemoteFile> remote,
        IReadOnlyList<LocalFile>? local = null,
        CancellationToken ct = default)
    {
        local ??= ScanLocal(ct);

        var warnings = new List<string>();

        // Índice de lo que hay en el iPod, por clave insensible a mayúsculas:
        // así es como FAT32 ve los nombres realmente.
        var localIndex = new Dictionary<string, LocalFile>();
        foreach (var f in local)
            localIndex[FatPath.ComparisonKey(f.RelativePath)] = f;

        var added = new List<SyncItem>();
        var updated = new List<SyncItem>();
        var unchanged = 0;

        // Lo que sobrevive acá es lo que el servidor quiere que exista en el
        // iPod. Lo que quede sin marcar en el índice local es candidato a borrar.
        var expected = new HashSet<string>();

        foreach (var r in remote)
        {
            ct.ThrowIfCancellationRequested();

            // El .m3u que define una playlist no va al iPod: su contenido se
            // guarda en el estado de la raíz. Ver FetchPlaylistDefinitions.
            if (_layout.IsPlaylistDefinition(r.RelativePath)) continue;

            var destRel = _layout.MapToIpodRelative(r.RelativePath);
            var key = FatPath.ComparisonKey(destRel);

            // Dos archivos distintos del servidor que caen en el mismo nombre
            // del iPod: se pisarían mutuamente en cada sync, alternando para
            // siempre. Mejor avisar y quedarse con el primero.
            if (!expected.Add(key))
            {
                warnings.Add($"Colisión de nombre en FAT32, se omite: {r.RelativePath}");
                continue;
            }

            if (!localIndex.TryGetValue(key, out var l))
            {
                added.Add(new SyncItem(SyncAction.Added, destRel, r.Size, r.LastWriteUtc)
                          { SourcePath = r.RelativePath });
                continue;
            }

            if (IsSame(l, r)) unchanged++;
            else updated.Add(new SyncItem(SyncAction.Updated, destRel, r.Size, r.LastWriteUtc)
                             { SourcePath = r.RelativePath });
        }

        var removed = new List<SyncItem>();
        if (_settings.DeleteRemovedFiles)
        {
            foreach (var l in local)
            {
                ct.ThrowIfCancellationRequested();
                if (expected.Contains(FatPath.ComparisonKey(l.RelativePath))) continue;
                if (_layout.IsIpodOnly(l.RelativePath)) continue;

                removed.Add(new SyncItem(SyncAction.Removed, l.RelativePath, l.Size, l.LastWriteUtc));
            }
        }

        return new SyncDiff(added, updated, removed, unchanged, warnings);
    }

    /// <summary>
    /// Un archivo se considera igual si coinciden tamaño y fecha dentro de la
    /// tolerancia de FAT32. No se compara hash: son 25 GB por SFTP.
    /// </summary>
    private static bool IsSame(LocalFile local, RemoteFile remote)
    {
        if (local.Size != remote.Size) return false;
        var delta = Math.Abs((local.LastWriteUtc - remote.LastWriteUtc).TotalSeconds);
        return delta <= FatTimeToleranceSeconds;
    }

    // ---------------------------------------------------------- playlists

    /// <summary>
    /// Baja a memoria los .m3u que definen las playlists. No se copian al
    /// iPod: van al estado de la raíz, y de ahí los lee el generador del
    /// catálogo. Así el dispositivo queda con música y playlists y nada más.
    /// </summary>
    public List<PlaylistDefinition> FetchPlaylistDefinitions(
        IReadOnlyList<RemoteFile> remote,
        List<string>? warnings = null,
        CancellationToken ct = default)
    {
        var result = new List<PlaylistDefinition>();

        foreach (var r in remote.Where(f => _layout.IsPlaylistDefinition(f.RelativePath))
                                .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            var folder = _layout.PlaylistFolderOf(r.RelativePath);
            if (folder is null) continue;

            var name = IpodLayout.PlaylistName(folder);

            // Una carpeta con dos .m3u adentro: nos quedamos con el primero
            // en orden alfabético, que es el que gana de forma estable.
            if (result.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                warnings?.Add($"La playlist '{name}' tiene más de un .m3u; se ignora {r.RelativePath}");
                continue;
            }

            try
            {
                var text = _sftp.ReadAllText(r.RelativePath, ct);
                result.Add(new PlaylistDefinition
                {
                    Name = name,
                    RemoteFolder = folder,
                    Lines = text.Replace("\r\n", "\n").Split('\n').ToList()
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                warnings?.Add($"No se pudo leer la playlist '{name}': {ex.Message}");
            }
        }

        return result;
    }

    // ------------------------------------------------------------ transfer

    public async Task<SyncResult> TransferAsync(
        SyncDiff diff,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var started = DateTime.UtcNow;
            var errors = new ConcurrentBag<string>();
            var items = diff.ToTransfer.ToList();

            var bytesTotal = diff.BytesToTransfer;
            var completedBytes = 0L;
            var filesCompleted = 0;
            var transferred = 0;

            // Bytes del archivo que cada índice tiene EN VUELO ahora mismo. Con
            // varios archivos bajando a la vez, el total para la barra de
            // progreso es lo ya terminado más la suma de lo que cada uno
            // lleva parcialmente — no hay un único "archivo actual".
            var inFlight = new ConcurrentDictionary<int, long>();
            var lastReportTicks = 0L;

            // `force` para los reportes que no se pueden perder: el de cada
            // archivo terminado, que es el que mueve el contador.
            void Report(int fileIndex, string relativePath, bool force = false)
            {
                if (progress is null) return;

                if (!force)
                {
                    // SSH.NET llama al callback de progreso por cada bloque
                    // recibido: miles de veces por segundo, y ahora por
                    // MaxParallelTransfers a la vez. La UI redibuja a 10 Hz
                    // (ver MainWindow), así que reportar más seguido que esto
                    // solo llena la cola del dispatcher con trabajo que se
                    // descarta — y esa cola la comparte con el resto de la UI.
                    var now = Environment.TickCount64;
                    var last = Interlocked.Read(ref lastReportTicks);
                    if (now - last < ProgressIntervalMs) return;

                    // Que solo uno de los workers pase por ventana: si pierde
                    // la carrera, el otro ya reportó recién.
                    if (Interlocked.CompareExchange(ref lastReportTicks, now, last) != last) return;
                }

                var bytesDone = Interlocked.Read(ref completedBytes) + inFlight.Values.Sum();
                progress.Report(new SyncProgress(
                    fileIndex, items.Count, relativePath,
                    bytesDone, bytesTotal, DateTime.UtcNow - started));
            }

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelTransfers,
                CancellationToken = ct
            };

            // Cada partición de Parallel.ForEach recibe su propia SftpService
            // (localInit/localFinally): nunca se comparte una conexión SFTP
            // entre threads. La original (_sftp), usada para listar el
            // servidor, no se toca acá — su ciclo de vida es de quien
            // construyó el motor.
            Parallel.ForEach(items, options,
                localInit: () => new SftpService(_settings),
                body: (item, state, index, localSftp) =>
                {
                    var i = (int)index;
                    var dest = FatPath.ToLocalPath(_layout.MountRoot, item.RelativePath);

                    try
                    {
                        EnsureWritable(dest);

                        var source = new RemoteFile(item.SourcePath, item.Size, item.RemoteLastWriteUtc);
                        localSftp.DownloadFile(source, dest, bytes =>
                        {
                            inFlight[i] = (long)bytes;
                            // +1: mientras está en vuelo todavía no cuenta como
                            // terminado, así que se reporta como "trabajando en
                            // el archivo número (completados + 1)".
                            Report(Volatile.Read(ref filesCompleted) + 1, item.RelativePath);
                        }, ct);

                        inFlight.TryRemove(i, out _);
                        Interlocked.Add(ref completedBytes, item.Size);
                        Interlocked.Increment(ref transferred);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        // Que un archivo falle no puede abortar el lote: con 1000
                        // archivos, perder la corrida entera por un permiso raro
                        // significa volver a empezar de cero.
                        errors.Add($"{item.RelativePath}: {ex.Message}");
                        inFlight.TryRemove(i, out _);
                        Interlocked.Add(ref completedBytes, item.Size);
                    }
                    finally
                    {
                        var doneCount = Interlocked.Increment(ref filesCompleted);
                        Report(doneCount, item.RelativePath, force: true);
                    }

                    return localSftp;
                },
                localFinally: localSftp => localSftp.Dispose());

            var errorList = errors.ToList();
            var deleted = DeleteRemoved(diff, errorList, ct);

            return new SyncResult(transferred, deleted, errorList.Count,
                                  Interlocked.Read(ref completedBytes), DateTime.UtcNow - started, errorList);
        }, ct);
    }

    private int DeleteRemoved(SyncDiff diff, List<string> errors, CancellationToken ct)
    {
        if (!_settings.DeleteRemovedFiles) return 0;

        var deleted = 0;
        foreach (var item in diff.Removed)
        {
            ct.ThrowIfCancellationRequested();
            var path = FatPath.ToLocalPath(_layout.MountRoot, item.RelativePath);
            try
            {
                EnsureWritable(path);
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex)
            {
                errors.Add($"No se pudo borrar {item.RelativePath}: {ex.Message}");
            }
        }

        if (deleted > 0)
            foreach (var root in _layout.WritableRoots)
                if (Directory.Exists(root)) PruneEmptyDirectories(root, ct);

        return deleted;
    }

    /// <summary>
    /// Borra las carpetas que quedaron vacías después de un borrado. Rockbox
    /// las sigue listando al navegar, así que el usuario ve álbumes fantasma
    /// que no tienen ni una canción adentro.
    /// </summary>
    private void PruneEmptyDirectories(string dir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var sub in Directory.EnumerateDirectories(dir))
            PruneEmptyDirectories(sub, ct);

        // Las raíces que administra el sync se quedan aunque queden vacías.
        if (_layout.WritableRoots.Any(r =>
                string.Equals(Path.GetFullPath(dir), Path.GetFullPath(r),
                              StringComparison.OrdinalIgnoreCase)))
            return;

        try
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                EnsureWritable(dir);
                Directory.Delete(dir);
            }
        }
        catch { /* carrera con el SO o volumen desconectado: no es crítico */ }
    }

    /// <summary>
    /// Última línea de defensa antes de cualquier escritura o borrado.
    /// El iPod ya se corrompió dos veces y su letra de unidad cambia entre
    /// sesiones: una ruta mal armada acá borra el disco equivocado.
    /// </summary>
    private void EnsureWritable(string path)
    {
        var full = Path.GetFullPath(path);

        foreach (var root in _layout.WritableRoots)
        {
            var prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        }

        throw new InvalidOperationException(
            $"Bloqueado por seguridad: '{full}' cae fuera de " +
            string.Join(" y ", _layout.WritableRoots.Select(r => $"'{r}'")) + ".");
    }
}
