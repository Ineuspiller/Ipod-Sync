using System.Runtime.InteropServices;

namespace IpodSync.Core.Devices;

/// <summary>
/// Arranque automático con la sesión del usuario. Difiere por completo entre
/// sistemas, así que va detrás de una interfaz: la lógica de la app nunca
/// pregunta en qué SO está corriendo.
/// </summary>
public interface IStartupManager
{
    /// <summary>Si no se puede consultar el estado, se asume que no.</summary>
    bool IsEnabled { get; }

    /// <summary>Qué se toca para lograrlo. Se muestra al usuario para que
    /// sepa qué le está cambiando la app en su sistema.</summary>
    string Description { get; }

    void Enable(string executablePath);

    void Disable();
}

public static class StartupManagerFactory
{
    public static IStartupManager Create() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsStartupManager()
            : new LinuxStartupManager();
}

/// <summary>
/// Entrada en la clave Run del usuario. Se prefiere al Programador de tareas
/// porque no necesita permisos de administrador y el usuario la puede
/// inspeccionar y borrar con msconfig o el Administrador de tareas.
/// </summary>
public class WindowsStartupManager : IStartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "IpodSync";

    public string Description => $@"HKCU\{RunKey}\{ValueName}";

    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is not null;
            }
            catch { return false; }
        }
    }

    public void Enable(string executablePath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Solo Windows.");

        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException("No se pudo abrir la clave Run.");

        // --minimized para que arranque en la bandeja y no tape la pantalla
        // en cada inicio de sesión.
        key.SetValue(ValueName, $"\"{executablePath}\" --minimized");
    }

    public void Disable()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* ya no estaba */ }
    }
}

/// <summary>
/// Archivo .desktop en ~/.config/autostart, que es el estándar de XDG y lo
/// respetan GNOME, KDE y Xfce. Se prefiere a una unidad de systemd porque
/// esto es una app gráfica: tiene que arrancar con la sesión, no con el
/// sistema.
/// </summary>
public class LinuxStartupManager : IStartupManager
{
    private const string FileName = "ipodsync.desktop";

    private static string AutostartDir => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart");

    private static string FilePath => Path.Combine(AutostartDir, FileName);

    public string Description => FilePath;

    public bool IsEnabled => File.Exists(FilePath);

    public void Enable(string executablePath)
    {
        Directory.CreateDirectory(AutostartDir);

        // Sin BOM y con saltos Unix: el parser de .desktop es estricto.
        File.WriteAllText(FilePath, string.Join('\n', new[]
        {
            "[Desktop Entry]",
            "Type=Application",
            "Name=IpodSync",
            "Comment=Sincroniza la biblioteca de Navidrome con el iPod",
            $"Exec=\"{executablePath}\" --minimized",
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            ""
        }), new System.Text.UTF8Encoding(false));
    }

    public void Disable()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { /* permisos raros: no es critico */ }
    }
}
