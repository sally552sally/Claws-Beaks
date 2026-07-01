using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Счётчики прогона. Сейчас — только тривиальный минимум (§4 нашего решения:
/// экономику/дроп пока не собираем). Расширяемый шов: добавить метрику = добавить поле
/// сюда и строчку в Summary(). Экономику (опыт/золото/дроп) прикрутим позже отдельно.
/// </summary>
public sealed class BotStats
{
    public int MobKills;    // побед над мобами
    public int Wins;        // все победы (мобы + будущий PvP)
    public int Losses;      // поражения (не смерть, просто проигранный бой)
    public int Deaths;      // смерти (HP<=0 → воскрешение в городе)
    public int Rejections;  // операции, отклонённые сервером (нет места/уровень/в бою/…)
    public int Errors;      // упавшие шаги/исключения
    public int Fights;      // всего боёв начато

    /// <summary>Сбросить перед новым прогоном.</summary>
    public void Reset()
    {
        MobKills = Wins = Losses = Deaths = Rejections = Errors = Fights = 0;
    }

    public string Summary()
        => $"Бои: {Fights} | Победы: {Wins} | Убито мобов: {MobKills} | " +
           $"Поражения: {Losses} | Смерти: {Deaths} | Отказы: {Rejections} | Ошибки: {Errors}";
}

/// <summary>Уровень сообщения — влияет только на цвет/префикс в логе.</summary>
public enum BotLogLevel { Info, Warn, Error, Step, Snapshot }

/// <summary>Интерфейс лога, чтобы шаги/операции не знали про конкретную реализацию (окно).</summary>
public interface IBotLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Step(string message);
    void Snapshot(string message);
}

/// <summary>
/// Реализация лога: копит строки в кольцевой буфер (для окна) и дублирует в Debug.Log.
/// Живёт на главном потоке (весь бот на нём), поэтому без блокировок.
/// </summary>
public sealed class BotLog : IBotLog
{
    private const int MAX_LINES = 500;

    private readonly List<string> mLines = new();

    /// <summary>Строки лога для отрисовки в окне (только чтение).</summary>
    public IReadOnlyList<string> Lines => mLines;

    /// <summary>Флаг «появилась новая строка» — окно использует, чтобы перерисоваться.</summary>
    public bool Dirty { get; private set; }

    public void Info(string message)     => Append(BotLogLevel.Info, message);
    public void Warn(string message)     => Append(BotLogLevel.Warn, message);
    public void Error(string message)    => Append(BotLogLevel.Error, message);
    public void Step(string message)     => Append(BotLogLevel.Step, message);
    public void Snapshot(string message) => Append(BotLogLevel.Snapshot, message);

    public void Clear()
    {
        mLines.Clear();
        Dirty = true;
    }

    /// <summary>Окно вызывает после перерисовки.</summary>
    public void ClearDirty() => Dirty = false;

    private void Append(BotLogLevel level, string message)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var prefix = level switch
        {
            BotLogLevel.Step     => "▶",
            BotLogLevel.Warn     => "⚠",
            BotLogLevel.Error     => "✖",
            BotLogLevel.Snapshot => "📊",
            _ => "·"
        };

        var line = $"[{stamp}] {prefix} {message}";
        mLines.Add(line);
        if (mLines.Count > MAX_LINES)
            mLines.RemoveRange(0, mLines.Count - MAX_LINES);

        Dirty = true;

        // Дублируем в консоль Unity — удобно при отладке самого бота.
        switch (level)
        {
            case BotLogLevel.Error: Debug.LogError($"[Bot] {message}"); break;
            case BotLogLevel.Warn:  Debug.LogWarning($"[Bot] {message}"); break;
            default:                Debug.Log($"[Bot] {message}"); break;
        }
    }
}
