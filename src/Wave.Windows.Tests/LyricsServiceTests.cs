using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using Wave.Windows.External;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests del LyricsService (v0.5.0).
///
/// Cubre el parser LRC (formato de timestamps), cálculo de línea activa,
/// y manejo de edge cases (líneas vacías, multi-timestamp, sin sincronización).
/// No testeamos el cliente HTTP real (lrclib.net) — eso requiere red.
/// </summary>
public class LyricsServiceTests
{
    private readonly LyricsService _service;

    public LyricsServiceTests()
    {
        // Usar un LrcLibClient con HttpClient mockeado (no se llamará en estos tests)
        var mockHttp = new HttpClient(new MockHttpHandler());
        var client = new LrcLibClient(mockHttp);
        _service = new LyricsService(client);
    }

    // === Parse: formato LRC básico ===

    [Fact]
    public void Parse_ConLetraSincronizada_DevuelveIsSyncedTrue()
    {
        // Arrange — formato LRC válido con timestamps
        var lrc = """
            [00:01.23] Primera línea
            [00:05.67] Segunda línea
            [00:10.00] Tercera línea
            """;

        // Act
        var result = _service.Parse(lrc);

        // Assert
        result.IsSynced.Should().BeTrue();
        result.Lines.Should().HaveCount(3);
        result.Lines[0].Text.Should().Be("Primera línea");
        result.Lines[0].TimestampMs.Should().Be(1230); // 1*60000 + 23*10
        result.Lines[1].Text.Should().Be("Segunda línea");
        result.Lines[1].TimestampMs.Should().Be(5670);
        result.Lines[2].Text.Should().Be("Tercera línea");
        result.Lines[2].TimestampMs.Should().Be(10_000);
    }

    [Fact]
    public void Parse_ConTimestampsDesordenados_LosOrdena()
    {
        // Arrange — timestamps en orden no secuencial
        var lrc = """
            [00:10.00] Tercera
            [00:01.00] Primera
            [00:05.00] Segunda
            """;

        // Act
        var result = _service.Parse(lrc);

        // Assert
        result.Lines.Should().HaveCount(3);
        result.Lines[0].Text.Should().Be("Primera");
        result.Lines[0].TimestampMs.Should().Be(1000);
        result.Lines[1].Text.Should().Be("Segunda");
        result.Lines[1].TimestampMs.Should().Be(5000);
        result.Lines[2].Text.Should().Be("Tercera");
        result.Lines[2].TimestampMs.Should().Be(10_000);
    }

    [Fact]
    public void Parse_ConTimestampsDe3DigitosMs_LosConvierteCorrectamente()
    {
        // Arrange — formato con milisegundos de 3 dígitos
        var lrc = "[00:01.123] Test";

        // Act
        var result = _service.Parse(lrc);

        // Assert — 1*60000 + 1*1000 + 123 = 6123 ms
        result.Lines[0].TimestampMs.Should().Be(6123);
    }

    [Fact]
    public void Parse_ConLineaVacia_SustituyePorNotaMusical()
    {
        // Arrange — línea con timestamp pero sin texto (instrumental)
        var lrc = "[00:01.00]\n[00:05.00] Letra";

        // Act
        var result = _service.Parse(lrc);

        // Assert
        result.Lines[0].Text.Should().Be("♪");
        result.Lines[1].Text.Should().Be("Letra");
    }

    [Fact]
    public void Parse_ConMultiplesTimestampsEnMismaLinea_LosExpande()
    {
        // Arrange — formato LRC comprimido (varios timestamps en una línea)
        var lrc = "[00:01.00][00:05.00][00:10.00] Misma letra";

        // Act
        var result = _service.Parse(lrc);

        // Assert: la misma letra se repite en 3 timestamps
        result.Lines.Should().HaveCount(3);
        result.Lines.Should().AllSatisfy(l => l.Text.Should().Be("Misma letra"));
        result.Lines[0].TimestampMs.Should().Be(1000);
        result.Lines[1].TimestampMs.Should().Be(5000);
        result.Lines[2].TimestampMs.Should().Be(10_000);
    }

    [Fact]
    public void Parse_ConLetrasPlanasSinTimestamps_DevuelveIsSyncedFalse()
    {
        // Arrange — letra sin timestamps (plain lyrics)
        var lrc = """
            Esto es la primera línea
            Esto es la segunda línea
            Esto es la tercera línea
            """;

        // Act
        var result = _service.Parse(lrc);

        // Assert
        result.IsSynced.Should().BeFalse();
        result.Lines.Should().BeEmpty();
        result.PlainLyrics.Should().NotBeNull();
        result.PlainLyrics.Should().Contain("primera línea");
        result.PlainLyrics.Should().Contain("tercera línea");
    }

    [Fact]
    public void Parse_ConTextoVacio_DevuelveVacio()
    {
        // Act
        var result = _service.Parse("");

        // Assert
        result.IsEmpty.Should().BeTrue();
        result.IsSynced.Should().BeFalse();
        result.Lines.Should().BeEmpty();
        result.PlainLyrics.Should().BeNull();
    }

    [Fact]
    public void Parse_ConSoloTimestampsYTambienTextoPlano_CombinaAmbos()
    {
        // Arrange — mezcla de líneas con y sin timestamp
        var lrc = """
            [00:01.00] Línea sincronizada
            Línea sin timestamp
            [00:05.00] Otra sincronizada
            """;

        // Act
        var result = _service.Parse(lrc);

        // Assert: tiene líneas sincronizadas Y texto plano
        result.IsSynced.Should().BeTrue();
        result.Lines.Should().HaveCount(2);
        result.PlainLyrics.Should().NotBeNull();
        result.PlainLyrics.Should().Contain("Línea sin timestamp");
    }

    // === ActiveLineIndex ===

    [Fact]
    public void ActiveLineIndex_ConPosicionExacta_DevuelveEsaLinea()
    {
        // Arrange
        var lrc = "[00:01.00] Primera\n[00:05.00] Segunda\n[00:10.00] Tercera";
        var lyrics = _service.Parse(lrc);

        // Act — posición = 5000ms (exactamente en la segunda línea)
        var index = _service.ActiveLineIndex(lyrics, 5000);

        // Assert
        index.Should().Be(1); // índice 0-based
    }

    [Fact]
    public void ActiveLineIndex_ConPosicionEntreLineas_DevuelveLineaAnterior()
    {
        // Arrange
        var lrc = "[00:01.00] Primera\n[00:05.00] Segunda\n[00:10.00] Tercera";
        var lyrics = _service.Parse(lrc);

        // Act — posición = 7000ms (entre segunda y tercera)
        var index = _service.ActiveLineIndex(lyrics, 7000);

        // Assert — debe devolver la línea 1 (última con timestamp <= 7000)
        index.Should().Be(1);
    }

    [Fact]
    public void ActiveLineIndex_ConPosicionAntesDePrimeraLinea_DevuelveMenos1()
    {
        // Arrange
        var lrc = "[00:05.00] Primera\n[00:10.00] Segunda";
        var lyrics = _service.Parse(lrc);

        // Act — posición = 1000ms (antes de la primera línea que es 5000ms)
        var index = _service.ActiveLineIndex(lyrics, 1000);

        // Assert
        index.Should().Be(-1);
    }

    [Fact]
    public void ActiveLineIndex_ConPosicionDespuesDeUltimaLinea_DevuelveUltima()
    {
        // Arrange
        var lrc = "[00:01.00] Primera\n[00:05.00] Segunda";
        var lyrics = _service.Parse(lrc);

        // Act — posición = 100_000ms (mucho después de la última línea)
        var index = _service.ActiveLineIndex(lyrics, 100_000);

        // Assert
        index.Should().Be(1);
    }

    [Fact]
    public void ActiveLineIndex_ConLetrasNoSincronizadas_DevuelveMenos1()
    {
        // Arrange
        var lrc = "Línea plana sin timestamps";
        var lyrics = _service.Parse(lrc);

        // Act
        var index = _service.ActiveLineIndex(lyrics, 5000);

        // Assert
        index.Should().Be(-1);
    }

    [Fact]
    public void ActiveLineIndex_ConLetrasVacias_DevuelveMenos1()
    {
        // Arrange
        var lyrics = _service.Parse("");

        // Act
        var index = _service.ActiveLineIndex(lyrics, 5000);

        // Assert
        index.Should().Be(-1);
    }

    [Fact]
    public void ActiveLineIndex_ConPosicionCeroYPrimeraLineaEnCero_DevuelveCero()
    {
        // Arrange
        var lrc = "[00:00.00] Inmediato\n[00:05.00] Segunda";
        var lyrics = _service.Parse(lrc);

        // Act
        var index = _service.ActiveLineIndex(lyrics, 0);

        // Assert
        index.Should().Be(0);
    }

    // === ParsedLyrics helpers ===

    [Fact]
    public void ParsedLyrics_IsEmpty_ConTodoVacio_RetornaTrue()
    {
        var lyrics = new ParsedLyrics(PlainLyrics: null, Lines: new(), IsSynced: false);
        lyrics.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ParsedLyrics_IsEmpty_ConSoloPlainLyrics_RetornaFalse()
    {
        var lyrics = new ParsedLyrics(PlainLyrics: "texto", Lines: new(), IsSynced: false);
        lyrics.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ParsedLyrics_IsEmpty_ConSoloLines_RetornaFalse()
    {
        var lyrics = new ParsedLyrics(
            PlainLyrics: null,
            Lines: new List<LyricLine> { new(0, "test") },
            IsSynced: true);
        lyrics.IsEmpty.Should().BeFalse();
    }

    /// <summary>
    /// HttpMessageHandler mock que devuelve 404 siempre (para que no hagamos requests reales en tests).
    /// </summary>
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
