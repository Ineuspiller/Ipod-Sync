using IpodSync.Core.Models;

namespace IpodSync.Desktop.Localization;

/// <summary>
/// Todo el texto de la UI, en los dos idiomas. Un diccionario a mano y no
/// .resx/satellite assemblies a propósito: la app no usa ningún framework de
/// binding (sin MVVM por ahora), así que un diccionario
/// simple que <see cref="L"/> consulta desde el code-behind encaja con el
/// resto del código y no agrega tooling nuevo al build.
///
/// Si falta una clave para el idioma pedido, <see cref="L.T"/> devuelve la
/// clave tal cual en vez de tirar una excepción: un texto sin traducir se
/// nota mirando la pantalla, una ventana que no abre no.
/// </summary>
internal static class Strings
{
    public static readonly Dictionary<string, (string Es, string En)> Table = new()
    {
        // ---------------------------------------------------- comunes
        ["common.error"]          = ("Error: {0}", "Error: {0}"),
        ["common.section.server"] = ("SERVIDOR", "SERVER"),
        ["common.section.ipod"]   = ("IPOD", "IPOD"),

        // ---------------------------------------------- ConnectionWindow
        ["conn.window.title"]     = ("iPod Sync — Conexión al servidor", "iPod Sync — Connect to server"),
        ["conn.heading"]          = ("Conectar al servidor", "Connect to server"),
        ["conn.subtitle"]         = ("Datos de acceso SSH al servidor donde corre Navidrome.",
                                      "SSH access details for the server running Navidrome."),
        ["conn.host.label"]       = ("Dirección o IP", "Address or IP"),
        ["conn.host.watermark"]   = ("192.168.1.10 o tu-servidor.ts.net", "192.168.1.10 or your-server.ts.net"),
        ["conn.port.label"]       = ("Puerto", "Port"),
        ["conn.user.label"]       = ("Usuario", "Username"),
        ["conn.user.watermark"]   = ("usuario", "username"),
        ["conn.section.auth"]    = ("AUTENTICACIÓN", "AUTHENTICATION"),
        ["conn.auth.password"]    = ("Contraseña", "Password"),
        ["conn.auth.key"]         = ("Clave SSH", "SSH key"),
        ["conn.password.hint"]    = ("Se guarda cifrada con DPAPI, atada a tu usuario de Windows.",
                                      "Stored encrypted with DPAPI, tied to your Windows user."),
        ["conn.keyfile.label"]    = ("Archivo de clave privada", "Private key file"),
        ["conn.browse"]           = ("Buscar...", "Browse..."),
        ["conn.browse.title"]     = ("Elegí la clave privada", "Choose the private key"),
        ["conn.passphrase.label"] = ("Passphrase (si la clave tiene)", "Passphrase (if the key has one)"),
        ["conn.section.library"]  = ("BIBLIOTECA", "LIBRARY"),
        ["conn.library.label"]    = ("Carpeta de música en el servidor", "Music folder on the server"),
        ["conn.library.watermark"]= ("/ruta/a/tu/biblioteca", "/path/to/your/library"),
        ["conn.test"]             = ("Probar conexión", "Test connection"),
        ["conn.testing"]          = ("Probando...", "Testing..."),
        ["conn.save"]             = ("Guardar y continuar", "Save and continue"),
        ["conn.language.label"]   = ("Idioma", "Language"),

        // Validación del formulario
        ["conn.err.host"]         = ("Falta la dirección del servidor.", "Server address is missing."),
        ["conn.err.port"]         = ("El puerto no es válido.", "The port is not valid."),
        ["conn.err.user"]         = ("Falta el usuario.", "Username is missing."),
        ["conn.err.library"]      = ("Falta la carpeta de música.", "Music folder is missing."),
        ["conn.err.keyfile"]      = ("Elegí el archivo de clave privada.", "Choose the private key file."),
        ["conn.err.password"]     = ("Ingresá la contraseña.", "Enter the password."),
        ["conn.err.unexpected"]   = ("Error inesperado: {0}", "Unexpected error: {0}"),

        // Resultado de la prueba de conexión (arma ConnectionResult.Outcome)
        ["conn.result.connecting"]  = ("Conectando al servidor...", "Connecting to server..."),
        ["conn.result.folderMissing"] = ("Conectó al servidor, pero no existe la carpeta:\n{0}",
                                          "Connected to the server, but the folder doesn't exist:\n{0}"),
        ["conn.result.success"]    = ("Conexión correcta.\n{0} carpetas y {1} archivos en el primer nivel.",
                                       "Connection successful.\n{0} folders and {1} files at the top level."),
        ["conn.result.authFailed"] = ("Falló la autenticación: {0}", "Authentication failed: {0}"),
        ["conn.result.connFailed"] = ("No se pudo conectar: {0}", "Could not connect: {0}"),
        ["conn.result.unknown"]    = ("Error: {0}", "Error: {0}"),

        // ------------------------------------------------------- MainWindow
        ["main.server.unchecked"]  = ("Sin verificar", "Not checked yet"),
        ["main.server.verifying"]  = ("Verificando...", "Checking..."),
        ["main.server.connected"]  = ("Conectado", "Connected"),
        ["main.ipod.searching"]    = ("Buscando...", "Searching..."),
        ["main.ipod.notfound"]     = ("No detectado", "Not detected"),
        ["main.ipod.found"]        = ("{0} en {1}", "{0} at {1}"),
        ["main.ipod.freegb"]       = ("{0:F1} GB libres de {1:F1} GB", "{0:F1} GB free of {1:F1} GB"),
        ["main.ipod.lastsync"]     = ("  ·  última sync {0} ({1} pistas)", "  ·  last sync {0} ({1} tracks)"),
        ["main.diskmode.hint"]     = ("Conectá el iPod en Modo Disco de Apple (apagalo y prendelo manteniendo SELECT + PLAY).",
                                       "Connect the iPod in Apple Disk Mode (turn it off, then on while holding SELECT + PLAY)."),
        ["main.autosync"]          = ("Sincronizar al conectar el iPod", "Sync when the iPod connects"),
        ["main.startup"]           = ("Arrancar con el sistema", "Start with the system"),
        ["main.delete"]            = ("Borrar del iPod lo que ya no está en el servidor",
                                       "Delete from the iPod what's no longer on the server"),
        ["main.delete.on"]         = ("Si sacás o renombrás algo en el servidor, también se borra del iPod.",
                                       "If you remove or rename something on the server, it's deleted from the iPod too."),
        ["main.delete.off"]        = ("El iPod puede ir acumulando archivos que ya no están en el servidor.",
                                       "The iPod may accumulate files that are no longer on the server."),
        ["main.settings"]          = ("Configuración", "Settings"),
        ["main.sync"]              = ("Sincronizar", "Sync"),
        ["main.startupError"]      = ("No se pudo cambiar: {0}", "Couldn't change it: {0}"),
        ["main.rescan"]            = ("Reescanear", "Rescan"),
        ["main.rescanning"]        = ("Reescaneando...", "Rescanning..."),
        ["main.library"]           = ("Explorar biblioteca", "Browse library"),
        ["main.library.hint"]      = ("Ver lo que ya está en el iPod.", "See what's already on the iPod."),

        // ---------------------------------------------------- LibraryPanel
        ["library.title"]          = ("Biblioteca", "Library"),
        ["library.close"]          = ("Volver", "Back"),
        ["library.mode.albums"]    = ("Álbumes", "Albums"),
        ["library.mode.songs"]     = ("Canciones", "Songs"),
        ["library.layout.list"]    = ("Lista", "List"),
        ["library.layout.grid"]    = ("Grilla", "Grid"),
        ["library.back"]           = ("< Álbumes", "< Albums"),
        ["library.trackCount"]     = ("{0} canciones", "{0} tracks"),
        ["library.trackCount.one"] = ("1 canción", "1 track"),
        ["library.empty"]          = ("El iPod todavía no tiene música. Sincronizá primero.",
                                       "The iPod doesn't have any music yet. Sync it first."),
        ["library.noDevice"]       = ("Conectá el iPod para ver su biblioteca.",
                                       "Connect the iPod to see its library."),
        ["library.fixCovers"]         = ("Arreglar carátulas", "Fix covers"),
        ["library.fixCovers.running"] = ("Buscando carátulas...", "Finding covers..."),
        ["library.fixCovers.result"]  = ("{0} carátulas nuevas, {1} álbumes sin ninguna carátula embebida.",
                                          "{0} new covers, {1} albums with no embedded cover at all."),
        ["library.fixCovers.none"]    = ("Ya estaban todas.", "They were all already there."),

        // -------------------------------------------------------- SyncWindow
        ["sync.window.title"]         = ("Sincronizar", "Sync"),
        ["sync.phase.analyzing"]      = ("Analizando...", "Analyzing..."),
        ["sync.phase.analyzing.detail"] = ("Listando la biblioteca del servidor.", "Listing the server's library."),
        ["sync.phase.uptodate"]       = ("Todo al día", "Up to date"),
        ["sync.phase.uptodate.detail"] = ("El iPod ya tiene la biblioteca completa. No se tocó ningún archivo.",
                                           "The iPod already has the full library. No file was touched."),
        ["sync.phase.confirm"]        = ("Listo para sincronizar", "Ready to sync"),
        ["sync.phase.confirm.detail"] = ("{0} archivos a copiar ({1:F2} GB).", "{0} files to copy ({1:F2} GB)."),
        ["sync.phase.confirm.delete"] = ("\nSe van a BORRAR {0} archivos que ya no están en el servidor.",
                                          "\n{0} files no longer on the server will be DELETED."),
        ["sync.phase.running"]        = ("Sincronizando...", "Syncing..."),
        ["sync.phase.running.detail"] = ("No desconectes el iPod.", "Don't disconnect the iPod."),
        ["sync.phase.cancelled"]      = ("Cancelado", "Cancelled"),
        ["sync.phase.cancelled.detail"] = ("No se transfirió nada.", "Nothing was transferred."),
        ["sync.phase.cancelled.detail2"] = ("Lo que ya se copió quedó completo. La próxima sync sigue desde ahí.",
                                             "What was already copied is complete. The next sync picks up from there."),
        ["sync.phase.analyzeError"]   = ("No se pudo analizar", "Couldn't analyze"),
        ["sync.phase.syncError"]      = ("Falló la sincronización", "Sync failed"),
        ["sync.phase.done"]           = ("Listo", "Done"),
        ["sync.phase.done.errors"]    = ("Terminó con errores", "Finished with errors"),
        ["sync.phase.done.detail"]    = ("Expulsá el iPod desde el sistema antes de desconectarlo.",
                                          "Eject the iPod from the system before disconnecting it."),
        ["sync.log.cancelledByUser"]  = ("Cancelado por el usuario.", "Cancelled by the user."),
        ["sync.log.transferred"]      = ("Transferidos: {0}", "Transferred: {0}"),
        ["sync.log.deleted"]          = ("Borrados: {0}", "Deleted: {0}"),
        ["sync.log.failed"]           = ("Fallidos: {0}", "Failed: {0}"),
        ["sync.log.playlists"]        = ("Playlists: {0} ({1} temas)", "Playlists: {0} ({1} tracks)"),
        ["sync.log.covers"]           = ("Carátulas completadas: {0}", "Covers filled in: {0}"),
        ["sync.summary"]              = ("{0} nuevos, {1} actualizados, {2} a borrar, {3} sin cambios ({4:F0} MB a transferir)",
                                          "{0} new, {1} updated, {2} to delete, {3} unchanged ({4:F0} MB to transfer)"),
        ["sync.cancel"]                = ("Cancelar", "Cancel"),
        ["sync.close"]                 = ("Cerrar", "Close"),

        // -------------------------------------------------------- bandeja
        ["tray.open"] = ("Abrir", "Open"),
        ["tray.sync"] = ("Sincronizar ahora", "Sync now"),
        ["tray.exit"] = ("Salir", "Exit"),

        // ----------------------------------------------------- SettingsWindow
        ["settings.editConnection"] = ("Editar conexión...", "Edit connection..."),
        ["settings.skipConfirm"]    = ("Sincronizar sin confirmar, incluso si hay que borrar archivos",
                                        "Sync without confirmation, even if files need to be deleted"),
        ["settings.skipConfirm.hint"] = (
            "Arriesgado: si el servidor no está disponible o la ruta está mal configurada, " +
            "esto puede borrar tu biblioteca del iPod sin avisarte.",
            "Risky: if the server is unreachable or the path is misconfigured, " +
            "this can delete your iPod library without warning you."),
    };

    public static string Raw(string key, AppLanguage lang)
    {
        if (!Table.TryGetValue(key, out var pair)) return key;
        return lang == AppLanguage.Spanish ? pair.Es : pair.En;
    }
}
