using IpodSync.Core.Devices;

namespace IpodSync.Core.Tests;

/// <summary>
/// El vigilante decide cuándo arranca una sincronización automática. Un
/// evento de más significa escribirle al iPod sin que nadie lo haya pedido.
/// </summary>
public class DeviceWatcherTests
{
    /// <summary>Detector controlado desde el test: no toca el disco.</summary>
    private class FakeDetector : IDeviceDetector
    {
        public IpodDevice? Device;
        public int Calls;

        public IpodDevice? Detect()
        {
            Calls++;
            return Device;
        }
    }

    private static IpodDevice At(string path) => new(path, "IPOD", 1, 2);

    private static DeviceWatcher Watcher(IDeviceDetector d) =>
        new(d, TimeSpan.FromMilliseconds(20));

    [Fact]
    public void Avisa_cuando_aparece_un_iPod()
    {
        var detector = new FakeDetector { Device = At("/media/IPOD") };
        using var w = Watcher(detector);

        IpodDevice? seen = null;
        w.Connected += d => seen = d;
        w.PollNow();

        Assert.NotNull(seen);
        Assert.Equal("/media/IPOD", seen!.MountPath);
        Assert.Equal("/media/IPOD", w.Current?.MountPath);
    }

    [Fact]
    public void No_repite_el_aviso_mientras_sigue_conectado()
    {
        // Si repitiera, el auto-sync se dispararia cada 3 segundos.
        var detector = new FakeDetector { Device = At("/media/IPOD") };
        using var w = Watcher(detector);

        var count = 0;
        w.Connected += _ => count++;

        w.PollNow();
        w.PollNow();
        w.PollNow();

        Assert.Equal(1, count);
    }

    [Fact]
    public void Avisa_cuando_se_desconecta()
    {
        var detector = new FakeDetector { Device = At("/media/IPOD") };
        using var w = Watcher(detector);
        w.PollNow();

        var gone = false;
        w.Disconnected += () => gone = true;

        detector.Device = null;
        w.PollNow();

        Assert.True(gone);
        Assert.Null(w.Current);
    }

    [Fact]
    public void Un_cambio_de_punto_de_montaje_cuenta_como_dispositivo_nuevo()
    {
        // Windows le cambia la letra de unidad al iPod entre sesiones: D:, E:,
        // F:. Si el vigilante no lo notara, el sync escribiria en la ruta vieja.
        var detector = new FakeDetector { Device = At("/media/D") };
        using var w = Watcher(detector);
        w.PollNow();

        var paths = new List<string>();
        w.Connected += d => paths.Add(d.MountPath);

        detector.Device = At("/media/E");
        w.PollNow();

        Assert.Equal(new[] { "/media/E" }, paths);
    }

    [Fact]
    public void En_pausa_no_dispara_nada()
    {
        // Durante una transferencia, escanear el disco compite con la
        // escritura y una deteccion espuria arrancaria una segunda sync.
        var detector = new FakeDetector { Device = At("/media/IPOD") };
        using var w = Watcher(detector);

        var fired = false;
        w.Connected += _ => fired = true;

        w.Paused = true;
        w.PollNow();

        Assert.False(fired);
        Assert.Equal(0, detector.Calls);
    }

    [Fact]
    public void Al_salir_de_pausa_vuelve_a_avisar()
    {
        var detector = new FakeDetector { Device = At("/media/IPOD") };
        using var w = Watcher(detector);
        w.Paused = true;
        w.PollNow();

        var fired = false;
        w.Connected += _ => fired = true;

        w.Paused = false;
        w.PollNow();

        Assert.True(fired);
    }

    [Fact]
    public void Un_detector_que_falla_no_tumba_el_vigilante()
    {
        // Pasa de verdad: la unidad se desconecta justo durante el escaneo.
        var detector = new ThrowingDetector();
        using var w = Watcher(detector);

        w.PollNow();          // no debe propagar
        Assert.Null(w.Current);
    }

    private class ThrowingDetector : IDeviceDetector
    {
        public IpodDevice? Detect() => throw new IOException("unidad desconectada");
    }

    [Fact]
    public async Task El_timer_sondea_solo_hasta_que_se_lo_detiene()
    {
        var detector = new FakeDetector { Device = At("/media/IPOD") };
        using var w = Watcher(detector);

        w.Start();
        await Task.Delay(120);
        w.Stop();

        var afterStop = detector.Calls;
        Assert.True(afterStop >= 2, $"Sondeo solo {afterStop} veces.");

        await Task.Delay(80);
        Assert.Equal(afterStop, detector.Calls);
    }
}
