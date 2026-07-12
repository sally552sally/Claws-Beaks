using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Канал лога — «про что» сообщение. Окно умеет фильтровать по каналам.</summary>
public enum BotChannel
{
    System,      // раннер, шаги, служебное
    Combat,      // бой
    Inventory,   // инвентарь / сундук / шмот
    Navigation,  // переходы по локациям
    Assert       // проверки (ассерты) и сухой прогон
}

/// <summary>Уровень сообщения — влияет на префикс/фильтр «только проблемы».</summary>
public enum BotLogLevel { Info, Warn, Error, Step, Snapshot }

/// <summary>Одна строка лога. Храним структурно, чтобы окно могло фильтровать/искать/экспортить.</summary>
public struct BotLogEntry
{
    public DateTime Time;
    public BotChannel Channel;
    public BotLogLevel Level;
    public string Message;

    public string Format()
    {
        var prefix = Level switch
        {
            BotLogLevel.Step => "▶",
            BotLogLevel.Warn => "⚠",
            BotLogLevel.Error => "✖",
            BotLogLevel.Snapshot => "📊",
            _ => "·"
        };
        return $"[{Time:HH:mm:ss}] {prefix} [{ChannelRu(Channel)}] {Message}";
    }

    public static string ChannelRu(BotChannel channel) => channel switch
    {
        BotChannel.Combat => "Бой",
        BotChannel.Inventory => "Инвентарь",
        BotChannel.Navigation => "Переход",
        BotChannel.Assert => "Проверка",
        _ => "Система"
    };
}

/// <summary>
/// Счётчики прогона + тайминги. Экономика (опыт/золото/дроп) сюда НЕ входит —
/// клиент её пока не видит; это расширяемый шов на потом.
/// </summary>
public sealed class BotStats
{
    public int MobKills;       // побед над мобами (PvE)
    public int PvpWins;        // побед над игроками (PvP)
    public int Wins;           // все победы (MobKills + PvpWins)
    public int Losses;         // поражения (PvE и PvP вместе)
    public int Deaths;         // смерти (HP<=0 → воскрешение)
    public int Rejections;     // операции, отклонённые сервером
    public int Errors;         // упавшие шаги/исключения
    public int Fights;         // всего боёв начато
    public int AssertsPassed;  // пройденные проверки
    public int AssertsFailed;  // проваленные проверки

    public int TotalTurns;        // суммарно моих ходов в боях
    public double FightSeconds;   // суммарное время в боях (сек)

    // Тайминги шагов: агрегируем по описанию шага (count + total сек).
    private readonly Dictionary<string, (int count, double seconds)> mStepAgg = new();

    public void Reset()
    {
        MobKills = PvpWins = Wins = Losses = Deaths = Rejections = Errors = Fights = 0;
        AssertsPassed = AssertsFailed = 0;
        TotalTurns = 0;
        FightSeconds = 0;
        mStepAgg.Clear();
    }

    /// <summary>Записать длительность одного выполнения шага (раннер зовёт после каждого шага).</summary>
    public void AddStepTiming(string describe, double seconds)
    {
        mStepAgg.TryGetValue(describe, out var agg);
        mStepAgg[describe] = (agg.count + 1, agg.seconds + seconds);
    }

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append($"Бои: {Fights} | Победы: {Wins} (Убито: {MobKills}, PvP: {PvpWins}) | Поражения: {Losses} | Смерти: {Deaths}");
        sb.Append($" | Отказы: {Rejections} | Ошибки: {Errors}");
        sb.Append($" | Проверки: ✓{AssertsPassed} ✗{AssertsFailed}");
        if (Fights > 0)
            sb.Append($" | Ср. бой: {FightSeconds / Fights:0.0}с / {(double)TotalTurns / Fights:0.0} ход.");
        return sb.ToString();
    }

    /// <summary>Отчёт по таймингам шагов: сколько раз, средняя и суммарная длительность.</summary>
    public string TimingsReport()
    {
        if (mStepAgg.Count == 0) return "— пока нет данных —";

        var sb = new StringBuilder();
        foreach (var kv in mStepAgg.OrderByDescending(k => k.Value.seconds).Take(15))
        {
            var (count, seconds) = kv.Value;
            sb.AppendLine($"{kv.Key}: ×{count}, ср. {seconds / count:0.0}с, всего {seconds:0.0}с");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Пути для файлов бота. Всё вне Assets/, чтобы Unity не импортировала логи как ассеты.</summary>
public static class BotPaths
{
    public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    public static string RunsDir => Path.Combine(ProjectRoot, "BotRuns");
    public static string ScreensDir => Path.Combine(RunsDir, "screens");
}

/// <summary>Интерфейс лога. Перегрузки без канала пишут в System.</summary>
public interface IBotLog
{
    void Write(BotChannel channel, BotLogLevel level, string message);

    void Info(string message);
    void Warn(string message);
    void Error(string message);
    void Step(string message);
    void Snapshot(string message);

    void Info(BotChannel channel, string message);
    void Warn(BotChannel channel, string message);
    void Error(BotChannel channel, string message);
}

/// <summary>
/// Реализация лога: структурные записи (канал/уровень/время) + Dirty-флаг для окна +
/// дублирование в Debug.Log + экспорт прогона в файл (BotRuns/*.log в корне проекта).
/// </summary>
public sealed class BotLog : IBotLog
{
    private const int MAX_LINES = 2000;

    private readonly List<BotLogEntry> mEntries = new();

    /// <summary>Записи для окна (только чтение).</summary>
    public IReadOnlyList<BotLogEntry> Entries => mEntries;

    /// <summary>Флаг «появилась новая строка» — окно перерисовывается по нему.</summary>
    public bool Dirty { get; private set; }

    public void Write(BotChannel channel, BotLogLevel level, string message)
    {
        mEntries.Add(new BotLogEntry
        {
            Time = DateTime.Now,
            Channel = channel,
            Level = level,
            Message = message
        });
        if (mEntries.Count > MAX_LINES)
            mEntries.RemoveRange(0, mEntries.Count - MAX_LINES);

        Dirty = true;

        switch (level)
        {
            case BotLogLevel.Error: Debug.LogError($"[Bot] {message}"); break;
            case BotLogLevel.Warn: Debug.LogWarning($"[Bot] {message}"); break;
            default: Debug.Log($"[Bot] {message}"); break;
        }
    }

    public void Info(string message) => Write(BotChannel.System, BotLogLevel.Info, message);
    public void Warn(string message) => Write(BotChannel.System, BotLogLevel.Warn, message);
    public void Error(string message) => Write(BotChannel.System, BotLogLevel.Error, message);
    public void Step(string message) => Write(BotChannel.System, BotLogLevel.Step, message);
    public void Snapshot(string message) => Write(BotChannel.System, BotLogLevel.Snapshot, message);

    public void Info(BotChannel channel, string message) => Write(channel, BotLogLevel.Info, message);
    public void Warn(BotChannel channel, string message) => Write(channel, BotLogLevel.Warn, message);
    public void Error(BotChannel channel, string message) => Write(channel, BotLogLevel.Error, message);

    public void Clear()
    {
        mEntries.Clear();
        Dirty = true;
    }

    /// <summary>Окно вызывает после перерисовки.</summary>
    public void ClearDirty() => Dirty = false;

    /// <summary>
    /// Экспортировать весь лог + сводку в файл. Возвращает путь к файлу (или null при ошибке).
    /// Файлы копятся в {корень проекта}/BotRuns/ — вне Assets, Unity их не трогает.
    /// </summary>
    public string ExportToFile(string scenarioName, string statsSummary, string timingsReport)
    {
        try
        {
            Directory.CreateDirectory(BotPaths.RunsDir);

            var safeName = string.Concat(scenarioName.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            var file = Path.Combine(BotPaths.RunsDir, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{safeName}.log");

            var sb = new StringBuilder();
            sb.AppendLine($"Сценарий: {scenarioName}");
            sb.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Сводка: {statsSummary}");
            sb.AppendLine("Тайминги шагов:");
            sb.AppendLine(timingsReport);
            sb.AppendLine(new string('─', 60));
            foreach (var entry in mEntries)
                sb.AppendLine(entry.Format());

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Bot] Экспорт лога не удался: {ex.Message}");
            return null;
        }
    }
}
