using Engine.Models;

namespace Engine.Services;

/// <summary>
/// Convierte ticks en fechas de juego y formatea fechas para mostrar.
/// Tick 0 = Día 1, Mes 1 (Enero), Año 1.
/// </summary>
public static class GameCalendar
{
    /// <summary>Convierte un tick en una GameDate.</summary>
    public static GameDate FromTick(long tick)
    {
        int totalDays = (int)(tick % (long)(GameDate.DaysPerYear * 10_000));
        int year      = totalDays / GameDate.DaysPerYear + 1;
        int dayOfYear = totalDays % GameDate.DaysPerYear;
        int month     = dayOfYear / GameDate.DaysPerMonth + 1;
        int day       = dayOfYear % GameDate.DaysPerMonth + 1;

        Season season = month switch
        {
            3 or 4 or 5   => Season.Spring,
            6 or 7 or 8   => Season.Summer,
            9 or 10 or 11 => Season.Autumn,
            _             => Season.Winter   // 12, 1, 2
        };

        return new GameDate(year, month, day, season);
    }

    /// <summary>Formatea una fecha para mostrar usando las claves de localización.</summary>
    public static string DisplayDate(GameDate date)
    {
        string monthName  = Loc.Get($"calendar.month.{date.Month}");
        string seasonName = Loc.Get($"calendar.season.{date.Season}");
        string yearLabel  = Loc.UI("year");
        return $"{date.Day} {monthName}, {yearLabel} {date.Year} ({seasonName})";
    }
}
