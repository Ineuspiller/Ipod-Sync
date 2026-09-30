using IpodSync.Core.Devices;
using IpodSync.Core.Models;
using IpodSync.Core.Services;

// Banco de pruebas de IpodSync.Core, sin UI.
//
// Sirve para entornos headless (por ejemplo, desarrollo por SSH en un
// servidor sin pantalla): ahí no se puede ejecutar Avalonia, pero sí ejercitar
// el motor contra el servidor real y contra una carpeta cualquiera haciendo de
// iPod, sin tener que pasar por la app de escritorio.
//
// Es además la base de la CLI headless para la Raspberry Pi del roadmap.

return Cli.Run(args);

static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }

        var opts = ParseOptions(args);
        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "detect" => Detect(),
                "test"   => Test(LoadSettings(opts)),
                "list"   => List(LoadSettings(opts), opts),
                "diff"   => Diff(LoadSettings(opts), opts),
                "sync"   => Sync(LoadSettings(opts), opts),
                "playlists" => Playlists(new SettingsService(opts.ConfigPath).Load(), opts),
                "covers" => Covers(new SettingsService(opts.ConfigPath).Load(), opts),
                "state"  => ShowState(opts),
                "config" => ShowConfig(opts),
                _        => Unknown(command)
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("\nCancelado.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nError: {ex.Message}");
            return 1;
        }
    }

    // ------------------------------------------------------------ comandos

    static int Detect()
    {
        var device = DeviceDetectorFactory.Create().Detect();
        if (device is null)
        {
            Console.WriteLine("No se detectó ningún iPod (se busca la carpeta .rockbox en la raíz).");
            return 1;
        }
        Console.WriteLine($"iPod: {device.Label}");
        Console.WriteLine($"  Ruta:  {device.MountPath}");
        Console.WriteLine($"  Libre: {device.FreeGb:F1} GB de {device.TotalGb:F1} GB");
        return 0;
    }

    static int Test(AppSettings settings)
    {
        using var sftp = new SftpService(settings);
        var result = sftp.TestConnectionAsync().GetAwaiter().GetResult();
        Console.WriteLine(result.Message);
        return result.Success ? 0 : 1;
    }

    static int List(AppSettings settings, Options opts)
    {
        using var sftp = new SftpService(settings);
        var files = ListRemote(sftp);

        Console.WriteLine($"\n{files.Count} archivos, {Gb(files.Sum(f => f.Size))}");

        foreach (var g in files.GroupBy(f => f.Extension)
                               .OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Key,-10} {g.Count(),6}  {Gb(g.Sum(f => f.Size))}");

        if (opts.Verbose)
            foreach (var f in files.Take(opts.Limit))
                Console.WriteLine($"  {f.Size,12}  {f.LastWriteUtc:yyyy-MM-dd HH:mm:ss}  {f.RelativePath}");

        return 0;
    }

    static int Diff(AppSettings settings, Options opts)
    {
        var target = RequireTarget(opts);
        using var sftp = new SftpService(settings);

        var remote = ListRemote(sftp);
        var engine = new SyncEngine(sftp, settings, target);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var local = engine.ScanLocal();
        var diff = engine.ComputeDiff(remote, local);
        sw.Stop();

        Console.WriteLine($"\nDestino: {engine.Layout.MountRoot}");
        Console.WriteLine($"  Albumes:   {engine.Layout.MusicRoot}");
        Console.WriteLine($"  Playlists: {engine.Layout.CatalogRoot}");
        Console.WriteLine($"Local:   {local.Count} archivos");
        Console.WriteLine($"Diff calculado en {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"\n{diff.Summary}");

        PrintWarnings(diff);
        PrintRenameBreakdown(diff, opts);

        if (opts.Verbose)
        {
            Show("NUEVOS",       diff.Added,   opts.Limit);
            Show("ACTUALIZADOS", diff.Updated, opts.Limit);
            Show("A BORRAR",     diff.Removed, opts.Limit);
        }
        else if (diff.HasChanges)
        {
            Console.WriteLine("\n(usar -v para ver el detalle, --renames para el desglose)");
        }

        return 0;
    }

    static int Sync(AppSettings settings, Options opts)
    {
        var target = RequireTarget(opts);
        if (opts.NoDelete) settings.DeleteRemovedFiles = false;

        using var sftp = new SftpService(settings);
        var remote = ListRemote(sftp);
        var engine = new SyncEngine(sftp, settings, target);
        var diff = engine.ComputeDiff(remote);

        Console.WriteLine($"\nDestino: {engine.Layout.MountRoot}");
        Console.WriteLine($"  Albumes:   {engine.Layout.MusicRoot}");
        Console.WriteLine($"  Playlists: {engine.Layout.CatalogRoot}");
        Console.WriteLine(diff.Summary);
        PrintWarnings(diff);
        PrintRenameBreakdown(diff, opts);

        // El pre-check barato del que habla el diseño: si no hay nada que
        // hacer, terminamos sin escribir un solo byte en el iPod. Las
        // definiciones de playlist son 5 archivos de texto: bajarlas para
        // comparar no cuesta nada al lado de no tener que tocar el iPod.
        var playlistWarnings = new List<string>();
        var definitions = engine.FetchPlaylistDefinitions(remote, playlistWarnings);

        if (!diff.HasChanges && !PlaylistsChanged(target, definitions))
        {
            Console.WriteLine("\nNada que hacer.");
            return 0;
        }

        if (!opts.Yes)
        {
            Console.Write($"\n¿Transferir {diff.ToTransfer.Count()} archivos" +
                          (diff.Removed.Count > 0 ? $" y BORRAR {diff.Removed.Count}" : "") +
                          "? [s/N] ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("s" or "si" or "y"))
            {
                Console.WriteLine("Abortado.");
                return 1;
            }
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var lastLine = 0;
        var progress = new Progress<SyncProgress>(p =>
        {
            var eta = p.Eta is { } t ? $" ETA {t:hh\\:mm\\:ss}" : "";
            var line = $"[{p.FileIndex}/{p.TotalFiles}] {p.Percent,5:F1}%{eta}  {Trim(p.CurrentFile, 60)}";
            Console.Write("\r" + line.PadRight(Math.Max(lastLine, line.Length)));
            lastLine = line.Length;
        });

        var result = engine.TransferAsync(diff, progress, cts.Token).GetAwaiter().GetResult();
        Console.WriteLine();

        Console.WriteLine($"\nTransferidos: {result.Transferred}");
        Console.WriteLine($"Borrados:     {result.Deleted}");
        Console.WriteLine($"Fallidos:     {result.Failed}");
        Console.WriteLine($"Bytes:        {Gb(result.BytesTransferred)}");
        Console.WriteLine($"Duración:     {result.Duration:hh\\:mm\\:ss}");

        foreach (var e in result.Errors.Take(opts.Limit))
            Console.Error.WriteLine($"  ! {e}");

        // El estado va en la raiz del iPod, no en la PC: asi la sincronizacion
        // funciona igual desde cualquier maquina. Lleva las definiciones de
        // playlist para poder rearmar el catalogo sin conexion.
        SyncStateService.Save(target, new SyncState
        {
            LastSyncUtc     = DateTime.UtcNow,
            SourceHost      = $"{settings.Username}@{settings.Host}",
            RemoteMusicPath = settings.RemoteMusicPath,
            TrackCount      = remote.Count(f => !engine.Layout.IsPlaylistDefinition(f.RelativePath)),
            TotalBytes      = remote.Sum(f => f.Size),
            LastDiffSummary = diff.Summary,
            Playlists       = definitions
        });

        foreach (var w in playlistWarnings.Take(opts.Limit)) Console.WriteLine($"  ! {w}");

        // El catalogo se rearma despues de transferir: recien ahi se sabe que
        // temas llegaron realmente al iPod y cuales hay que sacar de la lista.
        if (settings.BuildPlaylistCatalog) BuildCatalog(settings, target, opts);

        return result.Failed > 0 ? 1 : 0;
    }

    static int Playlists(AppSettings settings, Options opts)
    {
        // Rearma el catalogo desde el estado guardado: no necesita servidor.
        BuildCatalog(settings, RequireTarget(opts), opts);
        return 0;
    }

    /// <summary>
    /// Completa las caratulas faltantes leyendo la embebida de los propios
    /// temas del iPod. Es lo mismo que corre solo despues de cada sync y que
    /// el boton "Arreglar caratulas" del panel de biblioteca; aca sirve para
    /// probarlo sin UI (y para verificar que el publish recortado no rompio
    /// ni la lectura de tags ni la recodificacion de la imagen).
    /// </summary>
    static int Covers(AppSettings settings, Options opts)
    {
        var target = RequireTarget(opts);
        var albums = new LibraryBrowser().ScanAlbums(settings, target);
        var sinCaratula = albums.Count(a => a.CoverRelativePath is null);

        Console.WriteLine($"{albums.Count} albumes, {sinCaratula} sin caratula.");
        if (sinCaratula == 0) return 0;

        var musicRoot = new IpodLayout(settings, target).MusicRoot;
        var result = new CoverExtractor().FixMissingCovers(albums, musicRoot);

        Console.WriteLine($"Extraidas: {result.Extracted}");
        Console.WriteLine($"Sin ninguna embebida: {result.Skipped}");

        foreach (var e in result.Errors.Take(opts.Limit)) Console.WriteLine("  ! " + e);

        if (opts.Verbose)
            foreach (var a in albums.Where(a => a.CoverRelativePath is null))
                Console.WriteLine($"  sin caratula: {a.Name}");

        return 0;
    }

    static int ShowState(Options opts)
    {
        var target = RequireTarget(opts);
        var state = SyncStateService.Load(target);

        Console.WriteLine($"Archivo: {SyncStateService.PathFor(target)}");
        if (state.LastSyncUtc == default)
        {
            Console.WriteLine("  (todavia no se sincronizo este iPod)");
            return 0;
        }

        Console.WriteLine($"  Ultima sync: {state.LastSyncUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"  Origen:      {state.SourceHost}:{state.RemoteMusicPath}");
        Console.WriteLine($"  Pistas:      {state.TrackCount}");
        Console.WriteLine($"  Tamano:      {Gb(state.TotalBytes)}");
        Console.WriteLine($"  Ultimo diff: {state.LastDiffSummary}");
        Console.WriteLine($"  Playlists:   {state.Playlists.Count}");
        foreach (var pl in state.Playlists)
            Console.WriteLine($"    - {pl.Name} ({pl.Lines.Count(l => l.Trim().Length > 0)} lineas)");
        return 0;
    }

    /// <summary>Las playlists del servidor cambiaron respecto de lo guardado.</summary>
    static bool PlaylistsChanged(string mountRoot, List<PlaylistDefinition> fresh)
    {
        var stored = SyncStateService.Load(mountRoot).Playlists;
        if (stored.Count != fresh.Count) return true;

        foreach (var (a, b) in stored.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                         .Zip(fresh.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)))
        {
            if (!a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase)) return true;
            if (!a.Lines.SequenceEqual(b.Lines)) return true;
        }
        return false;
    }

    static void BuildCatalog(AppSettings settings, string mountRoot, Options opts)
    {
        var builder = new PlaylistCatalogBuilder(settings, mountRoot);
        var r = builder.Build();

        Console.WriteLine($"\nCatalogo de playlists: {builder.CatalogRoot}");
        Console.WriteLine($"  Escritas:   {r.PlaylistsWritten}");
        Console.WriteLine($"  Entradas:   {r.EntriesWritten}");
        Console.WriteLine($"  Descartadas:{r.EntriesDropped}");
        Console.WriteLine($"  Limpiadas:  {r.StaleRemoved}");

        foreach (var w in r.Warnings.Take(opts.Limit)) Console.WriteLine($"  ! {w}");
    }


    static int ShowConfig(Options opts)
    {
        var svc = new SettingsService(opts.ConfigPath);
        var s = svc.Load();
        Console.WriteLine($"Archivo: {svc.ConfigPath}");
        Console.WriteLine($"  Host:      {s.Username}@{s.Host}:{s.Port}");
        Console.WriteLine($"  Auth:      {s.AuthMode}" +
                          (s.AuthMode == AuthMode.PrivateKey ? $" ({s.PrivateKeyPath})" : ""));
        Console.WriteLine($"  Remoto:    {s.RemoteMusicPath}");
        Console.WriteLine($"  Configurado: {s.IsConfigured}");
        Console.WriteLine($"  Borrar lo que ya no está: {s.DeleteRemovedFiles}");
        return 0;
    }

    // ------------------------------------------------------------ auxiliares

    static List<RemoteFile> ListRemote(SftpService sftp)
    {
        Console.Write("Listando el servidor... ");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var files = sftp.ListAllFiles(new Progress<string>(m => Console.Write($"\rListando el servidor... {m}")));
        sw.Stop();
        Console.WriteLine($"\rListando el servidor... {files.Count} archivos en {sw.Elapsed.TotalSeconds:F1}s".PadRight(70));
        return files;
    }

    static void PrintWarnings(SyncDiff diff)
    {
        if (diff.Warnings.Count == 0) return;
        Console.WriteLine($"\n{diff.Warnings.Count} advertencia(s):");
        foreach (var w in diff.Warnings.Take(20)) Console.WriteLine($"  ! {w}");
    }

    /// <summary>
    /// Cuando hay muchos "nuevos" Y muchos "a borrar" en la misma corrida,
    /// separa lo que probablemente es el mismo archivo bajo otra ruta (mismo
    /// tamaño exacto en bytes) de lo que es contenido genuinamente distinto.
    /// Sin esto, "867 nuevos, 616 a borrar" es indistinguible a simple vista
    /// entre una reorganización sana y un choque de convención de nombres
    /// con otra herramienta de sync.
    /// </summary>
    static void PrintRenameBreakdown(SyncDiff diff, Options opts)
    {
        if (diff.Added.Count == 0 || diff.Removed.Count == 0) return;

        var b = RenameHints.Analyze(diff);
        if (b.RenameCount == 0) return;

        Console.WriteLine($"\nDe eso, {b.RenameCount} parecen ser el MISMO archivo en otra ruta " +
                          "(mismo tamaño exacto en bytes) y no contenido nuevo:");
        Console.WriteLine($"  Nuevos de verdad: {b.GenuinelyNew.Count}");
        Console.WriteLine($"  Sobran de verdad: {b.GenuinelyExtra.Count}");

        if (!opts.Verbose && !opts.ShowRenames) return;

        Console.WriteLine("\nPOSIBLES RENOMBRES (ruta vieja -> ruta nueva):");
        foreach (var r in b.LikelyRenames.Take(opts.Limit))
            Console.WriteLine($"  {r.Removed.RelativePath}\n    -> {r.Added.RelativePath}");
        if (b.LikelyRenames.Count > opts.Limit)
            Console.WriteLine($"  ... y {b.LikelyRenames.Count - opts.Limit} más");
    }

    static void Show(string title, IReadOnlyList<SyncItem> items, int limit)
    {
        if (items.Count == 0) return;
        Console.WriteLine($"\n{title} ({items.Count}):");
        foreach (var i in items.Take(limit)) Console.WriteLine($"  {i.RelativePath}");
        if (items.Count > limit) Console.WriteLine($"  ... y {items.Count - limit} más");
    }

    static string RequireTarget(Options opts) =>
        opts.Target ?? throw new ArgumentException(
            "Falta --target <carpeta>. Es la RAIZ del iPod (o una carpeta " +
            "comun que haga de raiz para probar).");

    static AppSettings LoadSettings(Options opts)
    {
        var s = new SettingsService(opts.ConfigPath).Load();
        if (opts.RemotePath is not null) s.RemoteMusicPath = opts.RemotePath;
        if (!s.IsConfigured)
            throw new InvalidOperationException(
                "La configuración está incompleta. Revisar con 'config' " +
                "o pasar --config <archivo.json>.");
        return s;
    }

    static string Gb(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1024d / 1024 / 1024:F2} GB"
                          : $"{bytes / 1024d / 1024:F1} MB";

    static string Trim(string s, int max) =>
        s.Length <= max ? s : "..." + s[^(max - 3)..];

    static int Unknown(string c)
    {
        Console.Error.WriteLine($"Comando desconocido: {c}\n");
        Usage();
        return 1;
    }

    // ------------------------------------------------------------ opciones

    record Options(string? Target, string? ConfigPath, string? RemotePath,
                   bool Verbose, bool Yes, bool NoDelete, bool ShowRenames, int Limit);

    static Options ParseOptions(string[] args)
    {
        string? target = null, config = null, remote = null;
        bool verbose = false, yes = false, noDelete = false, showRenames = false;
        var limit = 30;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target" or "-t": target = args[++i]; break;
                case "--config" or "-c": config = args[++i]; break;
                case "--remote":         remote = args[++i]; break;
                case "--limit":          limit = int.Parse(args[++i]); break;
                case "--verbose" or "-v": verbose = true; break;
                case "--yes" or "-y":     yes = true; break;
                case "--no-delete":       noDelete = true; break;
                case "--renames":         showRenames = true; break;
                default:
                    throw new ArgumentException($"Opción desconocida: {args[i]}");
            }
        }
        return new Options(target, config, remote, verbose, yes, noDelete, showRenames, limit);
    }

    static void Usage() => Console.WriteLine("""
        IpodSync CLI — banco de pruebas del motor, sin UI.

        Uso: ipodsync <comando> [opciones]

        Comandos:
          detect                 Busca un iPod conectado (carpeta .rockbox)
          config                 Muestra la configuración activa
          test                   Prueba la conexión con el servidor
          list                   Lista la biblioteca remota y resume por extensión
          diff   --target <dir>  Calcula el diff sin tocar nada (pre-check)
          sync   --target <dir>  Calcula el diff, transfiere y arma el catalogo
          playlists --target <dir>  Rearma el catalogo (sin servidor)
          covers --target <dir>  Extrae a cover.jpg las caratulas embebidas
                                 que falten (sin servidor)
          state  --target <dir>  Muestra el estado guardado en el iPod

        Opciones:
          -t, --target <dir>   RAIZ del iPod (o carpeta de prueba que haga de raíz).
                               La música va en <raíz>/Music y el catálogo de
                               playlists en <raíz>/Playlists.
          -c, --config <file>  Usa otro archivo de settings
              --remote <path>  Pisa la ruta remota de la config
          -v, --verbose        Muestra el detalle archivo por archivo
          -y, --yes            No pregunta antes de transferir
              --no-delete      No borra lo que ya no está en el servidor
              --renames        Muestra el detalle de los posibles renombres
              --limit <n>      Cuántas líneas de detalle mostrar (default 30)
        """);
}
