using System.Globalization;
using IpodSync.Core.Models;

namespace IpodSync.Core.Tests;

/// <summary>
/// El idioma por default sale del sistema operativo SOLO para una
/// configuración nueva: es lo que permite que alguien que no lea español
/// pueda usar la app antes de encontrar el selector de idioma.
/// CultureInfo.CurrentUICulture es un estado global del proceso: cada test
/// lo guarda y lo restaura para no filtrarle el idioma a otros tests.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void Detecta_espanol_del_sistema_operativo()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("es-AR");
            Assert.Equal(AppLanguage.Spanish, new AppSettings().Language);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void Sistema_no_espanol_cae_a_ingles_por_default()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");
            Assert.Equal(AppLanguage.English, new AppSettings().Language);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void IsConfigured_sigue_sin_depender_del_idioma()
    {
        var s = new AppSettings { Host = "h", Username = "u", RemoteMusicPath = "/x" };
        Assert.True(s.IsConfigured);
    }
}
