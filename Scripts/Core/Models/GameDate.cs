namespace Engine.Models;

public enum Season { Spring, Summer, Autumn, Winter }

/// <summary>
/// Fecha de juego derivada del tick actual. Inmutable.
/// 1 tick = 1 día | 1 mes = 30 días | 1 año = 360 días (12 × 30)
/// </summary>
public record GameDate(int Year, int Month, int Day, Season Season)
{
    public const int DaysPerMonth  = 30;
    public const int MonthsPerYear = 12;
    public const int DaysPerYear   = DaysPerMonth * MonthsPerYear; // 360

    public bool IsFirstDayOfMonth  => Day   == 1;
    public bool IsFirstDayOfYear   => Month == 1 && Day == 1;

    /// <summary>Primer día de cada estación (meses 3, 6, 9, 12).</summary>
    public bool IsFirstDayOfSeason => Day == 1 && (Month is 3 or 6 or 9 or 12);
}
