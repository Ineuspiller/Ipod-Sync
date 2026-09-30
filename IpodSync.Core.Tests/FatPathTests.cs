using IpodSync.Core.Services;

namespace IpodSync.Core.Tests;

public class FatPathTests
{
    [Theory]
    [InlineData("Album: Subtitle", "Album_ Subtitle")]
    [InlineData("What's Going On?", "What's Going On_")]
    [InlineData("AC/DC", "AC_DC")]          // la barra separa segmentos, no se pasa acá
    [InlineData("Hard*Core", "Hard_Core")]
    [InlineData("a<b>c|d\"e", "a_b_c_d_e")]
    public void SanitizeSegment_reemplaza_los_prohibidos_de_FAT32(string input, string expected)
        => Assert.Equal(expected, FatPath.SanitizeSegment(input.Replace('/', '_')));

    [Theory]
    [InlineData("Album.", "Album")]
    [InlineData("Album ", "Album")]
    [InlineData("Album. . ", "Album")]
    public void SanitizeSegment_recorta_puntos_y_espacios_finales(string input, string expected)
        => Assert.Equal(expected, FatPath.SanitizeSegment(input));

    [Fact]
    public void SanitizeSegment_nunca_devuelve_vacio()
        => Assert.NotEmpty(FatPath.SanitizeSegment("..."));

    [Fact]
    public void SanitizeSegment_escapa_nombres_reservados_de_Windows()
    {
        Assert.Equal("_CON.flac", FatPath.SanitizeSegment("CON.flac"));
        Assert.Equal("CONCIERTO.flac", FatPath.SanitizeSegment("CONCIERTO.flac"));
    }

    [Fact]
    public void SanitizeSegment_respeta_Unicode_no_ASCII()
    {
        // La biblioteca real tiene japonés, cirílico y comillas tipográficas.
        Assert.Equal("虹伝説2 - THE WHITE GOBLIN", FatPath.SanitizeSegment("虹伝説2 - THE WHITE GOBLIN"));
        Assert.Equal("Neuköln", FatPath.SanitizeSegment("Neuköln"));
    }

    [Fact]
    public void SanitizeRelativePath_limpia_cada_segmento_y_normaliza_separadores()
        => Assert.Equal("Album_ Live/01 Song_.flac",
                        FatPath.SanitizeRelativePath("Album: Live\\01 Song?.flac"));

    [Fact]
    public void ComparisonKey_ignora_mayusculas_porque_FAT32_las_ignora()
        => Assert.Equal(FatPath.ComparisonKey("Music/Song.flac"),
                        FatPath.ComparisonKey("music/SONG.FLAC"));

    [Fact]
    public void ComparisonKey_unifica_formas_de_normalizacion_Unicode()
    {
        // "Neuköln" compuesta (NFC) vs descompuesta (NFD). Si no se unifican,
        // el diff ve un archivo nuevo en cada corrida y recopia para siempre.
        var nfc = "Neuköln/01.flac";
        var nfd = "Neuköln/01.flac";
        Assert.NotEqual(nfc, nfd);
        Assert.Equal(FatPath.ComparisonKey(nfc), FatPath.ComparisonKey(nfd));
    }
}
