namespace IpodSync.Core.Devices;

/// <summary>
/// Vigila la conexión y desconexión del iPod.
///
/// El sondeo es barato a propósito: solo mira si existe una carpeta. No hay
/// forma portable de que el SO avise, y una notificación nativa distinta por
/// plataforma no vale la pena para algo que se enchufa una vez por día.
///
/// Los eventos se disparan desde un hilo del pool: quien los consuma en una
/// UI tiene que marshalear al hilo de interfaz.
/// </summary>
public class DeviceWatcher : IDisposable
{
    private readonly IDeviceDetector _detector;
    private readonly TimeSpan _interval;
    private readonly object _lock = new();

    private Timer? _timer;
    private bool _busy;

    public DeviceWatcher(IDeviceDetector? detector = null, TimeSpan? interval = null)
    {
        _detector = detector ?? DeviceDetectorFactory.Create();
        _interval = interval ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>El iPod conectado, o null. Lo actualiza el sondeo.</summary>
    public IpodDevice? Current { get; private set; }

    public event Action<IpodDevice>? Connected;
    public event Action? Disconnected;

    /// <summary>
    /// Si está en true, el sondeo no dispara eventos. Se usa durante una
    /// transferencia: escanear el disco compite con la escritura, y una
    /// detección espuria no puede arrancar una segunda sync encima.
    /// </summary>
    public bool Paused { get; set; }

    public void Start()
    {
        _timer ??= new Timer(_ => Poll(), null, TimeSpan.Zero, _interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Fuerza una lectura inmediata, sin esperar al próximo tick.</summary>
    public void PollNow() => Poll();

    private void Poll()
    {
        if (Paused) return;

        // Un sondeo lento (unidad que no responde) no puede encimarse con el
        // siguiente y terminar con dos escaneos del disco a la vez.
        lock (_lock)
        {
            if (_busy) return;
            _busy = true;
        }

        try
        {
            var found = _detector.Detect();
            var previous = Current;

            if (found?.MountPath == previous?.MountPath) return;

            Current = found;

            if (found is null) Disconnected?.Invoke();
            else Connected?.Invoke(found);
        }
        catch
        {
            // El dispositivo se desconectó justo durante el escaneo. Se
            // reintenta en el próximo tick.
        }
        finally
        {
            lock (_lock) _busy = false;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
