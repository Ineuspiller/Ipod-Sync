using IpodSync.Core.Devices;

namespace IpodSync.Core.Tests;

/// <summary>
/// El arranque automático toca la configuración del sistema del usuario, así
/// que importa que sea exacto y, sobre todo, que se pueda deshacer.
/// Solo se prueba la implementación de Linux: la de Windows escribe en el
/// registro y no hay registro acá.
/// </summary>
public class LinuxStartupManagerTests : IDisposable
{
    private readonly string _configHome;
    private readonly string? _previousConfigHome;

    public LinuxStartupManagerTests()
    {
        _previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _configHome = Path.Combine(Path.GetTempPath(), "ipodsync-xdg-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _configHome);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousConfigHome);
        try { Directory.Delete(_configHome, recursive: true); } catch { }
    }

    private string DesktopFile => Path.Combine(_configHome, "autostart", "ipodsync.desktop");

    [Fact]
    public void Arranca_desactivado()
        => Assert.False(new LinuxStartupManager().IsEnabled);

    [Fact]
    public void Activar_crea_el_desktop_en_la_carpeta_de_XDG()
    {
        var m = new LinuxStartupManager();

        m.Enable("/opt/ipodsync/IpodSync");

        Assert.True(m.IsEnabled);
        Assert.True(File.Exists(DesktopFile));
    }

    [Fact]
    public void El_desktop_tiene_las_claves_que_exige_el_estandar()
    {
        new LinuxStartupManager().Enable("/opt/ipodsync/IpodSync");

        var lines = File.ReadAllLines(DesktopFile);

        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.Contains("Type=Application", lines);
        Assert.Contains("Name=IpodSync", lines);
        Assert.Contains("Terminal=false", lines);
    }

    [Fact]
    public void Arranca_minimizado_para_no_tapar_la_pantalla_en_cada_login()
    {
        new LinuxStartupManager().Enable("/opt/ipodsync/IpodSync");

        Assert.Contains(File.ReadAllLines(DesktopFile),
                        l => l.StartsWith("Exec=") && l.Contains("--minimized"));
    }

    [Fact]
    public void Una_ruta_con_espacios_queda_entrecomillada()
    {
        // "C:\Program Files\..." y "/home/x/Mis Apps/..." son lo normal, no
        // el caso raro: sin comillas el .desktop ejecuta el programa
        // equivocado o directamente falla.
        new LinuxStartupManager().Enable("/home/user/Mis Apps/IpodSync");

        var exec = File.ReadAllLines(DesktopFile).First(l => l.StartsWith("Exec="));
        Assert.Contains("\"/home/user/Mis Apps/IpodSync\"", exec);
    }

    [Fact]
    public void El_archivo_no_lleva_BOM_ni_saltos_de_Windows()
    {
        // El parser de .desktop es estricto: con BOM no reconoce la primera
        // linea y la entrada se ignora en silencio.
        new LinuxStartupManager().Enable("/opt/ipodsync/IpodSync");

        var bytes = File.ReadAllBytes(DesktopFile);
        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    [Fact]
    public void Desactivar_deja_el_sistema_como_estaba()
    {
        var m = new LinuxStartupManager();
        m.Enable("/opt/ipodsync/IpodSync");

        m.Disable();

        Assert.False(m.IsEnabled);
        Assert.False(File.Exists(DesktopFile));
    }

    [Fact]
    public void Desactivar_sin_haber_activado_no_explota()
    {
        new LinuxStartupManager().Disable();
        Assert.False(new LinuxStartupManager().IsEnabled);
    }

    [Fact]
    public void Activar_dos_veces_no_duplica_nada()
    {
        var m = new LinuxStartupManager();

        m.Enable("/opt/a/IpodSync");
        m.Enable("/opt/b/IpodSync");

        Assert.Single(Directory.GetFiles(Path.Combine(_configHome, "autostart")));
        Assert.Contains("/opt/b/IpodSync", File.ReadAllText(DesktopFile));
    }

    [Fact]
    public void La_fabrica_devuelve_la_implementacion_del_sistema_actual()
    {
        var m = StartupManagerFactory.Create();

        if (OperatingSystem.IsWindows()) Assert.IsType<WindowsStartupManager>(m);
        else Assert.IsType<LinuxStartupManager>(m);
    }

    [Fact]
    public void Describe_que_le_toca_al_sistema_del_usuario()
    {
        // La app le cambia algo a la maquina: tiene que poder decir que.
        Assert.Contains("autostart", new LinuxStartupManager().Description);
    }
}
