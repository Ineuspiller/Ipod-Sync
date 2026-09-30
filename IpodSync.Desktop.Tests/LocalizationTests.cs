using Avalonia.Headless.XUnit;
using IpodSync.Core.Models;
using IpodSync.Core.Services;
using IpodSync.Desktop.Localization;

namespace IpodSync.Desktop.Tests;

/// <summary>
/// L.Current es estado global del proceso, compartido con las ventanas
/// reales durante toda la sesión de tests. Cada test que lo cambia lo
/// restaura en un finally para no filtrarle el idioma a otros tests.
/// </summary>
public class LocalizationTests
{
    private static void WithLanguage(AppLanguage lang, Action body)
    {
        var previous = L.Current;
        try { L.Set(lang); body(); }
        finally { L.Set(previous); }
    }

    [Fact]
    public void Ninguna_entrada_de_la_tabla_tiene_una_traduccion_vacia()
    {
        // Una clave con Es o En en blanco se ve al mirar la ventana en ese
        // idioma; mejor que el test lo diga antes que un usuario.
        foreach (var (key, (es, en)) in Strings.Table)
        {
            Assert.False(string.IsNullOrWhiteSpace(es), $"'{key}' sin texto en español");
            Assert.False(string.IsNullOrWhiteSpace(en), $"'{key}' sin texto en inglés");
        }
    }

    [Fact]
    public void Una_clave_inexistente_devuelve_la_clave_misma()
    {
        // Fallback a propósito: un texto sin traducir se nota mirando la
        // pantalla; una ventana que no abre por una KeyNotFoundException no.
        Assert.Equal("esto.no.existe", Strings.Raw("esto.no.existe", AppLanguage.Spanish));
    }

    [Fact]
    public void T_aplica_los_argumentos_de_formato()
    {
        WithLanguage(AppLanguage.Spanish, () =>
            Assert.Equal("Error: algo salió mal", L.T("common.error", "algo salió mal")));
    }

    [Fact]
    public void Set_no_dispara_Changed_si_el_idioma_no_cambia()
    {
        WithLanguage(AppLanguage.Spanish, () =>
        {
            var fired = false;
            L.Changed += () => fired = true;
            try { L.Set(AppLanguage.Spanish); }
            finally { L.Changed -= () => fired = true; }

            Assert.False(fired);
        });
    }

    [Fact]
    public void Set_dispara_Changed_una_sola_vez_cuando_cambia()
    {
        WithLanguage(AppLanguage.Spanish, () =>
        {
            var calls = 0;
            void Handler() => calls++;
            L.Changed += Handler;
            try { L.Set(AppLanguage.English); }
            finally { L.Changed -= Handler; }

            Assert.Equal(1, calls);
        });
    }

    [Theory]
    [InlineData(ConnectionOutcome.Success, AppLanguage.Spanish, "carpetas")]
    [InlineData(ConnectionOutcome.Success, AppLanguage.English, "folders")]
    [InlineData(ConnectionOutcome.FolderMissing, AppLanguage.Spanish, "no existe la carpeta")]
    [InlineData(ConnectionOutcome.FolderMissing, AppLanguage.English, "doesn't exist")]
    [InlineData(ConnectionOutcome.AuthFailed, AppLanguage.Spanish, "autenticación")]
    [InlineData(ConnectionOutcome.AuthFailed, AppLanguage.English, "Authentication")]
    [InlineData(ConnectionOutcome.ConnectionFailed, AppLanguage.Spanish, "No se pudo conectar")]
    [InlineData(ConnectionOutcome.ConnectionFailed, AppLanguage.English, "Could not connect")]
    public void Describe_arma_el_mensaje_en_el_idioma_activo(
        ConnectionOutcome outcome, AppLanguage lang, string expectedSubstring)
    {
        WithLanguage(lang, () =>
        {
            var result = new ConnectionResult(
                outcome == ConnectionOutcome.Success, "ignorado", outcome,
                Path: "/x", Dirs: 3, Files: 5, ExceptionMessage: "detalle");

            Assert.Contains(expectedSubstring, L.Describe(result));
        });
    }
}
