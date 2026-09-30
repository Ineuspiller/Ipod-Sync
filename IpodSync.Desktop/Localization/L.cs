using IpodSync.Core.Models;
using IpodSync.Core.Services;

namespace IpodSync.Desktop.Localization;

/// <summary>
/// Punto único de acceso al idioma activo. Las ventanas se suscriben a
/// <see cref="Changed"/> para poder re-traducirse sin reiniciar la app
/// cuando el usuario cambia el idioma desde cualquier lado.
/// </summary>
public static class L
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Spanish;

    public static event Action? Changed;

    /// <summary>
    /// Se llama una vez al arrancar, con el idioma guardado en settings.
    /// Separado de <see cref="Set"/> porque al arrancar no hay ventanas
    /// todavía suscriptas a <see cref="Changed"/>: no hace falta ni tiene
    /// sentido disparar el evento.
    /// </summary>
    public static void Initialize(AppLanguage language) => Current = language;

    public static void Set(AppLanguage language)
    {
        if (Current == language) return;
        Current = language;
        Changed?.Invoke();
    }

    /// <summary>Cambia el idioma y lo persiste en el momento: es una
    /// preferencia de toda la app, no algo atado a guardar el resto de un
    /// formulario. Lo usan tanto ConnectionWindow como MainWindow.</summary>
    public static void Choose(AppLanguage language, AppSettings settings)
    {
        if (settings.Language == language) return;
        settings.Language = language;
        App.Settings.Save(settings);
        Set(language);
    }

    /// <summary>Traduce una clave. Si no existe, devuelve la clave misma:
    /// un texto sin traducir se ve al mirar la ventana, no rompe nada.</summary>
    public static string T(string key) => Strings.Raw(key, Current);

    /// <summary>Traduce y aplica <see cref="string.Format"/> con los argumentos.</summary>
    public static string T(string key, params object?[] args) =>
        string.Format(T(key), args);

    /// <summary>
    /// Arma el mensaje de un resultado de conexión en el idioma activo.
    /// SftpService no conoce el idioma: solo expone qué pasó (Outcome) y los
    /// datos crudos (Path, Dirs, Files, ExceptionMessage). La CLI usa
    /// result.Message directamente y no pasa por acá.
    /// </summary>
    public static string Describe(ConnectionResult result) => result.Outcome switch
    {
        ConnectionOutcome.Success        => T("conn.result.success", result.Dirs, result.Files),
        ConnectionOutcome.FolderMissing  => T("conn.result.folderMissing", result.Path),
        ConnectionOutcome.AuthFailed     => T("conn.result.authFailed", result.ExceptionMessage),
        ConnectionOutcome.ConnectionFailed => T("conn.result.connFailed", result.ExceptionMessage),
        _                                 => T("conn.result.unknown", result.ExceptionMessage)
    };
}
