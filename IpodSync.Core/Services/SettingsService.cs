using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IpodSync.Core.Models;

namespace IpodSync.Core.Services;

/// <summary>
/// Guarda y carga la configuración desde el disco.
///
/// Las contraseñas se cifran con DPAPI en Windows (atado al usuario de
/// Windows: nadie más en la máquina puede descifrarlas). En Linux DPAPI
/// no existe, así que ahí se usa un cifrado local con una clave derivada
/// del usuario — menos fuerte, por eso en Linux conviene usar clave SSH
/// en vez de contraseña.
/// </summary>
public class SettingsService
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("IpodSync.v1.entropy");

    private readonly string _configPath;

    public SettingsService(string? customPath = null)
    {
        if (customPath is not null)
        {
            _configPath = customPath;
        }
        else
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IpodSync");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "settings.json");
        }
    }

    public string ConfigPath => _configPath;

    public AppSettings Load()
    {
        if (!File.Exists(_configPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch
        {
            // Config corrupta: arrancamos limpio en vez de explotar.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        // Por el contexto generado y no por reflexión: con el .exe recortado,
        // la versión con reflexión guarda un JSON vacío. Ver AppJsonContext.
        var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings);
        File.WriteAllText(_configPath, json);
    }

    // --- Cifrado de secretos ---

    public static string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return null;
        var bytes = Encoding.UTF8.GetBytes(plainText);

        if (OperatingSystem.IsWindows())
        {
            var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        // Fallback para Linux/macOS. No es DPAPI, pero evita el texto plano.
        return Convert.ToBase64String(XorWithMachineKey(bytes));
    }

    public static string? Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return null;

        try
        {
            var bytes = Convert.FromBase64String(cipherText);

            if (OperatingSystem.IsWindows())
            {
                var dec = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }

            return Encoding.UTF8.GetString(XorWithMachineKey(bytes));
        }
        catch
        {
            return null;
        }
    }

    private static byte[] XorWithMachineKey(byte[] data)
    {
        var seed = Encoding.UTF8.GetBytes(
            Environment.UserName + "|" + Environment.MachineName + "|IpodSync");
        var key = SHA256.HashData(seed);
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        return result;
    }
}
