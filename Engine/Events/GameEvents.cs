using Engine.Models;

namespace Engine.Events;

// ── Eventos de Población ─────────────────────────────────────────────────────
public record PopDiedEvent(PopGroup Pop, Province Province, string Cause)          : IGameEvent;
public record PopMobilityEvent(Province Province, string FromType, string ToType, int Count) : IGameEvent;
public record PopRadicalizedEvent(PopGroup Pop, Province Province)                 : IGameEvent;

// ── Eventos de Mercado ───────────────────────────────────────────────────────
/// <summary>Un bien de Supervivencia lleva N días sin stock.</summary>
public record MarketCrisisEvent(Province Province, string Good, int DaysWithoutStock) : IGameEvent;

// ── Eventos de Calendario ────────────────────────────────────────────────────
public record SeasonChangedEvent(Season Previous, Season Current, int Year) : IGameEvent;
public record YearEndEvent(int Year)                                         : IGameEvent;
