using System.Text;
using IpodSync.Core.Models;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace IpodSync.Core.Services;

public record RemoteFile(string RelativePath, long Size, DateTime LastWriteUtc)
{
    public string Name => Path.GetFileName(RelativePath);
    public string Extension => Path.GetExtension(RelativePath).ToLowerInvariant();
}

/// <summary>
/// Por qué salió como salió una prueba de conexión. <see cref="Message"/> ya
/// viene armado en español y es lo que usa la CLI tal cual; la UI de
/// escritorio arma su propio texto localizado a partir de <see cref="Outcome"/>
/// y los datos crudos, para que Core no le imponga un idioma a quien lo
/// consuma. Ver IpodSync.Desktop.Localization.
/// </summary>
public enum ConnectionOutcome { Success, FolderMissing, AuthFailed, ConnectionFailed, UnknownError }

public record ConnectionResult(
    bool Success,
    string Message,
    ConnectionOutcome Outcome = ConnectionOutcome.UnknownError,
    string? Path = null,
    int? Dirs = null,
    int? Files = null,
    string? ExceptionMessage = null)
{
    /// <summary>Cantidad de carpetas del primer nivel. Se usaba como señal de
    /// que la ruta apunta al lugar correcto.</summary>
    public int? TrackCount => Dirs;
}

/// <summary>
/// Cliente SFTP nativo. Reemplaza a rsync+WSL: se conecta directo al
/// servidor por SSH y lista/descarga archivos.
/// </summary>
public class SftpService : IDisposable
{
    private readonly AppSettings _settings;
    private SftpClient? _client;

    public SftpService(AppSettings settings) => _settings = settings;

    public bool IsConnected => _client?.IsConnected == true;

    private ConnectionInfo BuildConnectionInfo()
    {
        if (_settings.AuthMode == AuthMode.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(_settings.PrivateKeyPath))
                throw new InvalidOperationException("No se configuró la ruta de la clave privada.");
            if (!File.Exists(_settings.PrivateKeyPath))
                throw new FileNotFoundException($"No existe la clave: {_settings.PrivateKeyPath}");

            var pass = SettingsService.Unprotect(_settings.EncryptedKeyPassphrase);
            var keyFile = string.IsNullOrEmpty(pass)
                ? new PrivateKeyFile(_settings.PrivateKeyPath)
                : new PrivateKeyFile(_settings.PrivateKeyPath, pass);

            return new ConnectionInfo(_settings.Host, _settings.Port, _settings.Username,
                new PrivateKeyAuthenticationMethod(_settings.Username, keyFile));
        }

        var pwd = SettingsService.Unprotect(_settings.EncryptedPassword) ?? "";
        return new ConnectionInfo(_settings.Host, _settings.Port, _settings.Username,
            new PasswordAuthenticationMethod(_settings.Username, pwd));
    }

    public void Connect()
    {
        if (IsConnected) return;
        var info = BuildConnectionInfo();
        info.Timeout = TimeSpan.FromSeconds(15);
        _client = new SftpClient(info)
        {
            // Default de SSH.NET es 32KB. Con archivos FLAC grandes sobre un
            // link con latencia (una VPN, por ejemplo), eso significa miles
            // de round-trips por archivo. 256KB reduce esos round-trips sin
            // tocar la verificación de tamaño ni el patrón .tmp+rename: sigue
            // siendo el mismo dato, solo pedido en paquetes más grandes.
            BufferSize = 256 * 1024
        };
        _client.Connect();
    }

    public void Disconnect()
    {
        if (_client?.IsConnected == true) _client.Disconnect();
    }

    /// <summary>
    /// Prueba la conexión y verifica que la carpeta de música exista.
    /// Devuelve además cuántos archivos de audio hay en el primer nivel,
    /// como señal de que apunta al lugar correcto.
    /// </summary>
    public async Task<ConnectionResult> TestConnectionAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                Connect();

                if (!_client!.Exists(_settings.RemoteMusicPath))
                    return new ConnectionResult(false,
                        $"Conectó al servidor, pero no existe la carpeta:\n{_settings.RemoteMusicPath}",
                        ConnectionOutcome.FolderMissing, Path: _settings.RemoteMusicPath);

                var entries = _client.ListDirectory(_settings.RemoteMusicPath).ToList();
                var dirs = entries.Count(e => e.IsDirectory && e.Name != "." && e.Name != "..");
                var files = entries.Count(e => e.IsRegularFile);

                return new ConnectionResult(true,
                    $"Conexión correcta.\n{dirs} carpetas y {files} archivos en el primer nivel.",
                    ConnectionOutcome.Success, Dirs: dirs, Files: files);
            }
            catch (Renci.SshNet.Common.SshAuthenticationException ex)
            {
                return new ConnectionResult(false, $"Falló la autenticación: {ex.Message}",
                    ConnectionOutcome.AuthFailed, ExceptionMessage: ex.Message);
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                return new ConnectionResult(false, $"No se pudo conectar: {ex.Message}",
                    ConnectionOutcome.ConnectionFailed, ExceptionMessage: ex.Message);
            }
            catch (Exception ex)
            {
                return new ConnectionResult(false, $"Error: {ex.Message}",
                    ConnectionOutcome.UnknownError, ExceptionMessage: ex.Message);
            }
        }, ct);
    }

    /// <summary>
    /// Recorre recursivamente la carpeta remota y devuelve todos los
    /// archivos con su ruta relativa, tamaño y fecha. Es la base del
    /// diff incremental: se compara contra lo que hay en el iPod.
    /// </summary>
    public List<RemoteFile> ListAllFiles(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        Connect();
        var result = new List<RemoteFile>();
        var root = _settings.RemoteMusicPath.TrimEnd('/');
        Walk(root, root, result, progress, ct);
        return result;
    }

    private void Walk(string root, string current, List<RemoteFile> acc,
                      IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IEnumerable<ISftpFile> entries;
        try { entries = _client!.ListDirectory(current); }
        catch { return; }   // permisos, symlink roto, etc: seguimos

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.Name is "." or "..") continue;

            if (e.IsDirectory)
            {
                Walk(root, e.FullName, acc, progress, ct);
            }
            else if (e.IsRegularFile)
            {
                var rel = e.FullName.Substring(root.Length).TrimStart('/');
                if (IsExcluded(rel)) continue;
                acc.Add(new RemoteFile(rel, e.Length, e.LastWriteTimeUtc));

                if (acc.Count % 200 == 0)
                    progress?.Report($"{acc.Count} archivos encontrados...");
            }
        }
    }

    private bool IsExcluded(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        foreach (var pattern in _settings.ExcludePatterns)
        {
            if (pattern.StartsWith("*."))
            {
                if (name.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Sufijo de los archivos a medio bajar. Publico porque el motor de sync
    /// tiene que saber ignorarlos al escanear el iPod: un .tmp huérfano no es
    /// ni un archivo válido ni un archivo de más para borrar.
    /// </summary>
    public const string TempSuffix = ".ipodsync-tmp";

    /// <summary>
    /// Descarga un archivo remoto. Escribe primero a un .tmp y recién al
    /// terminar lo renombra al nombre final: si se corta la conexión o se
    /// cancela, no queda un archivo truncado que Rockbox intente reproducir
    /// y que el próximo diff dé por bueno.
    /// </summary>
    public void DownloadFile(RemoteFile file, string localPath,
                             Action<ulong>? onProgress = null,
                             CancellationToken ct = default)
    {
        Connect();
        var remote = _settings.RemoteMusicPath.TrimEnd('/') + "/" + file.RelativePath.Replace('\\', '/');

        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmpPath = localPath + TempSuffix;

        try
        {
            // Un .tmp de una corrida anterior que murió a mitad no sirve:
            // SSH.NET no reanuda descargas, así que lo pisamos desde cero.
            using (var fs = File.Create(tmpPath))
            {
                // OJO: el overload síncrono DownloadFile(path, stream, Action<ulong>)
                // despacha ese callback en un ThreadPool aparte (ThreadPoolProgress
                // interno de SSH.NET). Cancelar tirando ct.ThrowIfCancellationRequested()
                // DESDE ADENTRO de ese callback, como se hacía antes, nunca vuelve
                // por esta pila: queda como excepción no observada en un thread
                // ajeno y tira abajo el proceso ENTERO (verificado: SIGABRT). La
                // versión Async sí usa el CancellationToken de verdad — la
                // cancelación cancela la descarga en curso en vez de esperar a
                // que un callback explote.
                if (onProgress is null)
                {
                    _client!.DownloadFileAsync(remote, fs, ct).GetAwaiter().GetResult();
                }
                else
                {
                    var progress = new Progress<DownloadFileProgressReport>(
                        r => onProgress((ulong)r.TotalBytesDownloaded));
                    _client!.DownloadFileAsync(remote, fs, progress, ct).GetAwaiter().GetResult();
                }
            }

            // Verificación barata contra truncamiento silencioso.
            var actual = new FileInfo(tmpPath).Length;
            if (actual != file.Size)
                throw new IOException(
                    $"Descarga incompleta de '{file.RelativePath}': " +
                    $"se esperaban {file.Size} bytes y llegaron {actual}.");

            // Sin esto el diff incremental no sirve: el archivo local quedaría
            // fechado al momento de la descarga, nunca coincidiría con el remoto
            // y la sync siguiente recopiaría la biblioteca entera. FAT32 redondea
            // a 2 segundos, de ahí la tolerancia al comparar (ver SyncEngine).
            File.SetLastWriteTimeUtc(tmpPath, file.LastWriteUtc);

            // El rename es lo que publica el archivo. Dentro del mismo volumen
            // es atómico y conserva la fecha que acabamos de estampar.
            File.Move(tmpPath, localPath, overwrite: true);
        }
        catch
        {
            TryDelete(tmpPath);
            throw;
        }
    }

    /// <summary>
    /// Baja un archivo de texto a memoria, sin tocar el disco. Se usa para
    /// los .m3u: su contenido va al estado del iPod, no al dispositivo.
    /// </summary>
    public string ReadAllText(string relativePath, CancellationToken ct = default)
    {
        Connect();
        var remote = _settings.RemoteMusicPath.TrimEnd('/') + "/" + relativePath.Replace('\\', '/');

        using var ms = new MemoryStream();
        _client!.DownloadFileAsync(remote, ms, ct).GetAwaiter().GetResult();

        var text = new UTF8Encoding(false).GetString(ms.ToArray());
        return text.TrimStart('\uFEFF');   // algunos .m3u vienen con BOM
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* si el volumen ya se desconectó no hay nada que hacer */ }
    }

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }
}
