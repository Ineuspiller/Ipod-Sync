using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

namespace IpodSync.Core.Models;

/// <summary>
/// Contexto de serialización generado en tiempo de compilación para los dos
/// tipos que la app guarda en disco.
///
/// No es una optimización de estilo: `JsonSerializer.Serialize&lt;T&gt;` usa
/// reflexión, y el trimmer del publish no puede ver qué propiedades hacen
/// falta, así que las descarta (warning IL2026). Con el .exe recortado eso
/// se traduce en settings.json y .ipod-sync-state.json guardándose vacíos:
/// la app se olvidaría del servidor configurado en cada arranque. El
/// generador emite el código de (de)serialización a mano, sin reflexión, y
/// el trimmer entonces sí lo conserva.
///
/// UnsafeRelaxedJsonEscaping: los nombres de álbum tienen japonés, cirílico
/// y comillas tipográficas; sin esto el JSON queda ilegible lleno de \uXXXX.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SyncState))]
public partial class AppJsonContext : JsonSerializerContext
{
    /// <summary>La misma configuración pero sin escapar de más, para lo que
    /// va al iPod y puede llegar a leer una persona.</summary>
    public static readonly AppJsonContext Relaxed = new(new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}
