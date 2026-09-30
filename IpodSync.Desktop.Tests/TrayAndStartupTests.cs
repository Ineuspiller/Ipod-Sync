using Avalonia.Headless.XUnit;
using Avalonia.Platform;

namespace IpodSync.Desktop.Tests;

/// <summary>
/// Lo que agrega v0.3: ícono propio y controles de bandeja. Los toggles de
/// arranque/auto-sync/borrado viven ahora en el panel de Settings de
/// MainWindow — ver WindowLocalizationTests y AppLifecycleTests.
/// </summary>
public class TrayAndStartupTests
{
    [AvaloniaFact]
    public void El_icono_esta_empaquetado_como_recurso()
    {
        // Si falta, la bandeja queda sin ícono y la ventana con el genérico,
        // pero la app compila igual: solo se nota al ejecutarla.
        var uri = new Uri("avares://IpodSync/Assets/ipodsync.ico");

        Assert.True(AssetLoader.Exists(uri));

        using var stream = AssetLoader.Open(uri);
        Assert.True(stream.Length > 0);
    }
}
