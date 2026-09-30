using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IpodSync.Desktop.Views;

namespace IpodSync.Desktop.Tests;

public class ConnectionWindowTests
{
    [AvaloniaFact]
    public void Volver_de_clave_SSH_a_contrasena_muestra_el_panel_correcto()
    {
        // Bug real reportado: clickear Clave SSH y despues Contrasena dejaba
        // el panel de SSH visible. La causa era leer KeyRadio.IsChecked
        // desde el handler en vez de fijarse quien disparo el evento: el
        // grupo de radios no garantiza que el hermano ya este desmarcado en
        // ese instante.
        var window = new ConnectionWindow();
        var keyRadio = window.GetControl<RadioButton>("KeyRadio");
        var passwordRadio = window.GetControl<RadioButton>("PasswordRadio");
        var passwordPanel = window.GetControl<StackPanel>("PasswordPanel");
        var keyPanel = window.GetControl<StackPanel>("KeyPanel");

        keyRadio.IsChecked = true;
        Assert.True(keyPanel.IsVisible);
        Assert.False(passwordPanel.IsVisible);

        passwordRadio.IsChecked = true;

        Assert.True(passwordPanel.IsVisible);
        Assert.False(keyPanel.IsVisible);
    }

    [AvaloniaFact]
    public void Ida_y_vuelta_varias_veces_siempre_termina_en_el_panel_correcto()
    {
        var window = new ConnectionWindow();
        var keyRadio = window.GetControl<RadioButton>("KeyRadio");
        var passwordRadio = window.GetControl<RadioButton>("PasswordRadio");
        var passwordPanel = window.GetControl<StackPanel>("PasswordPanel");
        var keyPanel = window.GetControl<StackPanel>("KeyPanel");

        for (var i = 0; i < 3; i++)
        {
            keyRadio.IsChecked = true;
            passwordRadio.IsChecked = true;
        }

        Assert.True(passwordPanel.IsVisible);
        Assert.False(keyPanel.IsVisible);
    }
}
