using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using IpodSync.Core.Devices;
using IpodSync.Core.Models;
using IpodSync.Desktop.Views;

namespace IpodSync.Desktop.Tests;

/// <summary>
/// MainWindow, SettingsWindow y SyncWindow eran tres ventanas separadas.
/// Un usuario real las vio abiertas y superpuestas al mismo tiempo y no le
/// encontró sentido: pidió que todo cambie dentro de la misma ventana. Estos
/// tests verifican exactamente eso —que abrir Settings o Sync cambia el
/// panel visible de la MISMA MainWindow, no crea nada nuevo— y el arreglo de
/// un bug real que salió de la misma sesión: el botón de cerrar de Settings
/// quedaba tapado en una ventana de tamaño fijo sin scroll.
/// </summary>
public class MainWindowPanelsTests
{
    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public void Al_arrancar_solo_el_panel_principal_esta_visible()
    {
        var window = new MainWindow();

        Assert.True(window.GetControl<Control>("MainPanel").IsVisible);
        Assert.False(window.GetControl<Control>("SettingsPanel").IsVisible);
        Assert.False(window.GetControl<Control>("SyncPanel").IsVisible);
    }

    [AvaloniaFact]
    public void Abrir_settings_cambia_el_panel_sin_crear_ventana_nueva()
    {
        var window = new MainWindow();

        Click(window.GetControl<Button>("SettingsButton"));

        Assert.False(window.GetControl<Control>("MainPanel").IsVisible);
        Assert.True(window.GetControl<Control>("SettingsPanel").IsVisible);
        Assert.False(window.GetControl<Control>("SyncPanel").IsVisible);
    }

    [AvaloniaFact]
    public void Cerrar_settings_vuelve_al_panel_principal()
    {
        var window = new MainWindow();
        Click(window.GetControl<Button>("SettingsButton"));

        Click(window.GetControl<Button>("SettingsCloseButton"));

        Assert.True(window.GetControl<Control>("MainPanel").IsVisible);
        Assert.False(window.GetControl<Control>("SettingsPanel").IsVisible);
    }

    [AvaloniaFact]
    public void El_panel_de_settings_esta_en_un_ScrollViewer()
    {
        // El bug real que motivó esto: con una ventana de tamaño fijo y sin
        // scroll, el botón "Cerrar" (el último control del panel) quedaba
        // fuera del área visible en una máquina con fuente/DPI distinta a la
        // que se pudo probar acá. GetControl<ScrollViewer> falla si
        // SettingsPanel dejara de ser un ScrollViewer.
        var window = new MainWindow();

        var scroller = window.GetControl<ScrollViewer>("SettingsPanel");

        Assert.NotNull(scroller);
    }

    [AvaloniaFact]
    public void ShowSyncPanel_cambia_el_panel_sin_crear_ventana_nueva()
    {
        var window = new MainWindow();
        var device = new IpodDevice("/tmp/no-existe", "IPOD", 1, 2);

        window.ShowSyncPanel(new AppSettings(), device, autoStart: false);

        Assert.False(window.GetControl<Control>("MainPanel").IsVisible);
        Assert.False(window.GetControl<Control>("SettingsPanel").IsVisible);
        Assert.True(window.GetControl<Control>("SyncPanel").IsVisible);
        Assert.True(window.IsSyncing);
    }

    [AvaloniaFact]
    public void Cancelar_la_sync_vuelve_al_panel_principal_y_libera_el_guardia()
    {
        var window = new MainWindow();
        var device = new IpodDevice("/tmp/no-existe", "IPOD", 1, 2);
        window.ShowSyncPanel(new AppSettings(), device, autoStart: false);

        var closedRaised = false;
        window.SyncPanelClosed += () => closedRaised = true;

        Click(window.GetControl<Button>("SyncSecondaryButton"));

        Assert.True(window.GetControl<Control>("MainPanel").IsVisible);
        Assert.False(window.GetControl<Control>("SyncPanel").IsVisible);
        Assert.False(window.IsSyncing);
        Assert.True(closedRaised);
    }

    [AvaloniaFact]
    public void El_boton_de_reescanear_el_servidor_existe_y_no_reinicia_nada()
    {
        // El pedido concreto: poder revisar el servidor de nuevo sin
        // reiniciar la app ni pasar por ConnectionWindow. La sincronización
        // en sí ya vuelve a listar el servidor entero en cada corrida —esto
        // es solo el indicador de estado de la pantalla principal.
        var window = new MainWindow();

        var rescan = window.GetControl<Button>("RescanButton");
        Assert.NotNull(rescan);

        // No debe tirar excepción al clickearlo, sin configuración válida
        // (MainWindow con settings por default, como en los demás tests).
        Click(rescan);
    }
}
