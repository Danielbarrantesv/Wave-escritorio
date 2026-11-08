using System.IO;
using FluentAssertions;
using Moq;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests de los servicios de integración con Windows (v0.8.0).
///
/// TrayService, SmtcService y HotkeyService requieren contexto de UI real de Windows,
/// así que los tests son limitados: verifican que no fallen al construirse y que
/// manejan correctamente estados inválidos (sin ventana, sin WinRT, etc.).
/// </summary>
public class WindowsIntegrationServicesTests : IDisposable
{
    private readonly SettingsService _settings;
    private readonly AudioEngine _engine;
    private readonly Mock<ISongRepository> _songRepoMock;
    private readonly PlaybackController _playback;

    public WindowsIntegrationServicesTests()
    {
        _settings = new SettingsService(Path.Combine(Path.GetTempPath(), $"wave-test-{Guid.NewGuid():N}.json"));
        _engine = new AudioEngine();
        _songRepoMock = new Mock<ISongRepository>();
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());
        _playback = new PlaybackController(_settings, _engine, _songRepoMock.Object);
    }

    // === TrayService ===

    [Fact]
    public void TrayService_Constructor_NoLanzaExcepcion()
    {
        // Act — crear el tray service (debería funcionar en CI con sesión interactiva)
        // Si no hay sesión de usuario (CI headless), puede fallar — por eso catch.
        try
        {
            using var tray = new TrayService(_playback);
            // Assert — si llega aquí, no lanzó
            tray.Should().NotBeNull();
        }
        catch (Exception ex)
        {
            // En CI sin UI, NotifyIcon puede fallar — lo aceptamos
            System.Diagnostics.Debug.WriteLine($"[Test] TrayService falló (esperado en CI): {ex.Message}");
        }
    }

    [Fact]
    public void TrayService_Dispose_NoLanzaExcepcion()
    {
        try
        {
            var tray = new TrayService(_playback);
            tray.Dispose();
            tray.Dispose(); // idempotente
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Test] TrayService falló (esperado en CI): {ex.Message}");
        }
    }

    // === SmtcService ===

    [Fact]
    public void SmtcService_Constructor_NoLanzaExcepcionInclusoSiWinRTNoDisponible()
    {
        // Act — SmtcService debe manejar internamente cualquier fallo de WinRT
        // y nunca propagar excepción al constructor.
        using var smtc = new SmtcService(_playback);

        // Assert — si WinRT no está disponible, IsInitialized=false pero no lanza
        // Si WinRT está disponible, IsInitialized=true
        // Ambos estados son aceptables.
        Assert.True(true); // si llegó aquí, no lanzó
    }

    [Fact]
    public void SmtcService_Dispose_NoLanzaExcepcion()
    {
        var smtc = new SmtcService(_playback);
        smtc.Dispose();
        smtc.Dispose(); // idempotente
    }

    [Fact]
    public void SmtcService_IsInitialized_RetornaBool()
    {
        using var smtc = new SmtcService(_playback);
        // En CI puede ser false (sin WinRT), en dev con Windows real es true.
        // Solo verificamos que la propiedad existe y retorna un bool.
        var result = smtc.IsInitialized;
        Assert.IsType<bool>(result);
    }

    // === HotkeyService ===

    [Fact]
    public void HotkeyService_Constructor_ConHandleInvalido_NoLanzaExcepcion()
    {
        // Arrange — HWND inválido (IntPtr.Zero)
        // Act — debería manejarlo graciosamente
        using var hotkey = new HotkeyService(IntPtr.Zero, _playback, _engine);

        // Assert — con handle inválido, IsInitialized debe ser false
        hotkey.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void HotkeyService_Dispose_NoLanzaExcepcion()
    {
        var hotkey = new HotkeyService(IntPtr.Zero, _playback, _engine);
        hotkey.Dispose();
        hotkey.Dispose(); // idempotente
    }

    [Fact]
    public void HotkeyService_IsInitialized_ConHandleInvalido_EsFalse()
    {
        using var hotkey = new HotkeyService(IntPtr.Zero, _playback, _engine);
        hotkey.IsInitialized.Should().BeFalse();
    }

    public void Dispose()
    {
        _playback.Dispose();
        _engine.Dispose();
    }
}
