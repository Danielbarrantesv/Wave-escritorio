namespace Wave.Windows.Models;

/// <summary>
/// Una banda del ecualizador.
/// Implementa un filtro biquad peaking EQ.
/// </summary>
public class EqualizerBand
{
    /// <summary>Frecuencia central en Hz (ej: 60, 230, 910, 3600, 14000).</summary>
    public float Frequency { get; set; }

    /// <summary>Ancho de banda en octavas (0.5 = medio octavo).</summary>
    public float Bandwidth { get; set; } = 0.5f;

    /// <summary>Ganancia en dB (rango típico: -12 a +12).</summary>
    public float Gain { get; set; }
}
