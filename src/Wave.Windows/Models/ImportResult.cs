namespace Wave.Windows.Models;

/// <summary>
/// Resultado de una operación de importación de música.
/// </summary>
/// <param name="Added">Archivos nuevos insertados en la DB.</param>
/// <param name="Duplicates">Archivos ignorados por ser duplicados.</param>
/// <param name="Total">Total de archivos procesados.</param>
public record ImportResult(int Added, int Duplicates, int Total)
{
    public double ProgressPercent => Total > 0 ? (Added * 100.0 / Total) : 0;
}
