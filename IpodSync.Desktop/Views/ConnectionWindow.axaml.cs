using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using IpodSync.Core.Models;
using IpodSync.Core.Services;
using IpodSync.Desktop.Localization;

namespace IpodSync.Desktop.Views;

public partial class ConnectionWindow : Window
{
    private AppSettings _settings;
    private bool _testPassed;

    public ConnectionWindow()
    {
        InitializeComponent();
        _settings = App.Settings.Load();

        // El selector de idioma tiene que andar antes que nada más: esta es
        // la primera pantalla que ve alguien que capaz no lee español.
        LanguageBox.SelectedIndex = _settings.Language == AppLanguage.English ? 1 : 0;
        ApplyLanguage();
        L.Changed += ApplyLanguage;

        LoadIntoForm();
    }

    protected override void OnClosed(EventArgs e)
    {
        L.Changed -= ApplyLanguage;
        base.OnClosed(e);
    }

    private void ApplyLanguage()
    {
        Title                 = L.T("conn.window.title");
        HeadingText.Text      = L.T("conn.heading");
        SubtitleText.Text     = L.T("conn.subtitle");
        LanguageLabel.Text    = L.T("conn.language.label");

        ServerSectionText.Text = L.T("common.section.server");
        HostLabel.Text        = L.T("conn.host.label");
        HostBox.Watermark     = L.T("conn.host.watermark");
        PortLabel.Text        = L.T("conn.port.label");
        UserLabel.Text        = L.T("conn.user.label");
        UserBox.Watermark     = L.T("conn.user.watermark");

        AuthSectionText.Text  = L.T("conn.section.auth");
        PasswordRadio.Content = L.T("conn.auth.password");
        KeyRadio.Content      = L.T("conn.auth.key");
        PasswordLabel.Text    = L.T("conn.auth.password");
        PasswordHintText.Text = L.T("conn.password.hint");
        KeyFileLabel.Text     = L.T("conn.keyfile.label");
        BrowseButton.Content  = L.T("conn.browse");
        PassphraseLabel.Text  = L.T("conn.passphrase.label");

        LibrarySectionText.Text = L.T("conn.section.library");
        LibraryLabel.Text     = L.T("conn.library.label");
        RemotePathBox.Watermark = L.T("conn.library.watermark");

        // No pisar el texto mientras una prueba está en curso: el botón dice
        // "Probando..." en ese momento y un cambio de idioma a mitad no
        // debería hacerlo saltar de vuelta a "Probar conexión".
        if (TestButton.IsEnabled) TestButton.Content = L.T("conn.test");
        SaveButton.Content = L.T("conn.save");
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        var lang = LanguageBox.SelectedIndex == 1 ? AppLanguage.English : AppLanguage.Spanish;
        L.Choose(lang, _settings);
    }

    private void LoadIntoForm()
    {
        HostBox.Text       = _settings.Host;
        PortBox.Text       = _settings.Port.ToString();
        UserBox.Text       = _settings.Username;
        RemotePathBox.Text = _settings.RemoteMusicPath;
        KeyPathBox.Text    = _settings.PrivateKeyPath ?? "";

        if (_settings.AuthMode == AuthMode.PrivateKey)
        {
            KeyRadio.IsChecked = true;
        }
        else
        {
            PasswordRadio.IsChecked = true;
            PasswordBox.Text = SettingsService.Unprotect(_settings.EncryptedPassword) ?? "";
        }
    }

    private void OnAuthModeChanged(object? sender, RoutedEventArgs e)
    {
        // Los paneles pueden ser null durante la construcción del XAML.
        if (PasswordPanel is null || KeyPanel is null) return;

        // Quién es el sender, no KeyRadio.IsChecked: los dos radios disparan
        // este mismo Checked, y al pasar de Clave a Contraseña, el Checked de
        // PasswordRadio llega ANTES de que KeyRadio termine de desmarcarse
        // (es el propio grupo el que coordina el orden). Leer KeyRadio acá
        // devolvía "true" todavía y el panel se quedaba trabado en SSH.
        var useKey = ReferenceEquals(sender, KeyRadio);
        PasswordPanel.IsVisible = !useKey;
        KeyPanel.IsVisible = useKey;
        InvalidateTest();
    }

    private async void OnBrowseKey(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L.T("conn.browse.title"),
                AllowMultiple = false
            });

            if (files.Count > 0)
            {
                KeyPathBox.Text = files[0].Path.LocalPath;
                InvalidateTest();
            }
        }
        catch (Exception ex)
        {
            // async void: si el diálogo del SO falla y la excepción se
            // escapa, no la cataloga nadie y se cae la app entera. Siempre
            // queda poder pegar la ruta a mano en el cuadro de texto.
            ShowStatus(L.T("conn.err.unexpected", ex.Message), false);
        }
    }

    private void InvalidateTest()
    {
        _testPassed = false;
        SaveButton.IsEnabled = false;
    }

    private bool CollectFromForm(out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(HostBox.Text))
        { error = L.T("conn.err.host"); return false; }

        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        { error = L.T("conn.err.port"); return false; }

        if (string.IsNullOrWhiteSpace(UserBox.Text))
        { error = L.T("conn.err.user"); return false; }

        if (string.IsNullOrWhiteSpace(RemotePathBox.Text))
        { error = L.T("conn.err.library"); return false; }

        _settings.Host = HostBox.Text!.Trim();
        _settings.Port = port;
        _settings.Username = UserBox.Text!.Trim();
        _settings.RemoteMusicPath = RemotePathBox.Text!.Trim().TrimEnd('/');

        if (KeyRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(KeyPathBox.Text))
            { error = L.T("conn.err.keyfile"); return false; }

            _settings.AuthMode = AuthMode.PrivateKey;
            _settings.PrivateKeyPath = KeyPathBox.Text!.Trim();
            _settings.EncryptedKeyPassphrase = SettingsService.Protect(PassphraseBox.Text);
            _settings.EncryptedPassword = null;
        }
        else
        {
            if (string.IsNullOrEmpty(PasswordBox.Text))
            { error = L.T("conn.err.password"); return false; }

            _settings.AuthMode = AuthMode.Password;
            _settings.EncryptedPassword = SettingsService.Protect(PasswordBox.Text);
            _settings.EncryptedKeyPassphrase = null;
        }

        return true;
    }

    private void ShowStatus(string message, bool success)
    {
        StatusBorder.IsVisible = true;
        StatusText.Text = message;
        StatusText.Foreground = success
            ? new SolidColorBrush(Color.Parse("#4CAF50"))
            : new SolidColorBrush(Color.Parse("#FF6B6B"));
    }

    private async void OnTestConnection(object? sender, RoutedEventArgs e)
    {
        if (!CollectFromForm(out var error))
        {
            ShowStatus(error, false);
            return;
        }

        TestButton.IsEnabled = false;
        TestButton.Content = L.T("conn.testing");
        ShowStatus(L.T("conn.result.connecting"), true);

        try
        {
            using var sftp = new SftpService(_settings);
            var result = await sftp.TestConnectionAsync();

            ShowStatus(L.Describe(result), result.Success);
            _testPassed = result.Success;
            SaveButton.IsEnabled = result.Success;
        }
        catch (Exception ex)
        {
            ShowStatus(L.T("conn.err.unexpected", ex.Message), false);
            _testPassed = false;
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestButton.Content = L.T("conn.test");
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!_testPassed) return;
        if (!CollectFromForm(out var error))
        {
            ShowStatus(error, false);
            return;
        }

        App.Settings.Save(_settings);

        if (Avalonia.Application.Current is App app)
        {
            // Arranca el vigilante del iPod recién ahora que hay configuración
            // válida: antes de esto no tenía sentido sondear nada.
            app.EnterConfiguredState();

            // Por App.ShowMain(), no `new MainWindow()`: esa ventana quedaba
            // fuera de App._main, así que un sync disparado después —bandeja,
            // auto-sync, o el propio botón "Sincronizar" de ESTA MISMA
            // ventana, que llama a App.TryOpenSync— abría una SEGUNDA
            // MainWindow en vez de actualizar la que el usuario tiene en
            // pantalla.
            app.ShowMain();
        }

        Close();
    }
}
