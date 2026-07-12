using System;
using System.Collections.Generic;

/// <summary>Тип параметра — определяет, каким полем окно его нарисует.</summary>
public enum BotParamKind
{
    Int,       // числовое поле
    Float,     // числовое поле с дробью
    Bool,      // чекбокс
    Text,      // строка
    Location,  // выпадашка реальных кодов локаций (тянется с сервера)
    SetId      // выпадашка реальных SetId из рюкзака/сундука (тянется с сервера)
}

/// <summary>Описание одного параметра: имя (видно в окне), тип, дефолт.</summary>
public sealed class BotParamSpec
{
    public string Name;
    public BotParamKind Kind;
    public object DefaultValue;
}

/// <summary>
/// Параметры сценария. Позволяют менять числа/локации/сеты ИЗ ОКНА, без правки кода
/// и перекомпиляции (и без вылета из Play Mode из-за domain reload).
///
/// Как это работает (паттерн «двойной вызов»):
///   1) Окно вызывает метод сценария с BotParams.Collector() — каждый p.Int/p.Location/…
///      регистрирует свой параметр и возвращает дефолт. Так окно узнаёт, какие поля рисовать.
///   2) На Start окно вызывает метод ещё раз с BotParams.With(значения из полей) —
///      теперь p.Int/… возвращают то, что ввёл пользователь.
/// Объявление = использование, отдельной схемы не нужно.
///
/// Пример в сценарии:
///   public static BotScenario Farm(BotParams p)
///   {
///       var loc  = p.Location("Боевая локация", "loc_forest");
///       var mobs = p.Int("Мобов за заход", 10);
///       return Scenario("Фарм").GoTo(loc).KillMobs(mobs).Build();
///   }
/// </summary>
public sealed class BotParams
{
    private readonly bool mCollecting;
    private readonly Dictionary<string, object> mValues;
    private readonly List<BotParamSpec> mSpecs = new();

    /// <summary>Собранные описания параметров (после вызова в режиме Collector).</summary>
    public IReadOnlyList<BotParamSpec> Specs => mSpecs;

    private BotParams(bool collecting, Dictionary<string, object> values)
    {
        mCollecting = collecting;
        mValues = values ?? new Dictionary<string, object>();
    }

    /// <summary>Режим сбора описаний (окно узнаёт, какие поля рисовать).</summary>
    public static BotParams Collector() => new(true, null);

    /// <summary>Режим подстановки значений из окна.</summary>
    public static BotParams With(Dictionary<string, object> values) => new(false, values);

    public int Int(string name, int defaultValue)
        => Get(name, BotParamKind.Int, defaultValue, v => Convert.ToInt32(v));

    public float Float(string name, float defaultValue)
        => Get(name, BotParamKind.Float, defaultValue, v => Convert.ToSingle(v));

    public bool Bool(string name, bool defaultValue)
        => Get(name, BotParamKind.Bool, defaultValue, v => Convert.ToBoolean(v));

    public string Text(string name, string defaultValue)
        => Get(name, BotParamKind.Text, defaultValue, v => v?.ToString());

    /// <summary>Код локации — окно нарисует выпадашку реальных локаций с карты.</summary>
    public string Location(string name, string defaultValue)
        => Get(name, BotParamKind.Location, defaultValue, v => v?.ToString());

    /// <summary>SetId — окно нарисует выпадашку реальных сетов из рюкзака/сундука.</summary>
    public long SetId(string name, long defaultValue)
        => Get(name, BotParamKind.SetId, defaultValue, v => Convert.ToInt64(v));

    private T Get<T>(string name, BotParamKind kind, T defaultValue, Func<object, T> convert)
    {
        if (mCollecting)
        {
            mSpecs.Add(new BotParamSpec { Name = name, Kind = kind, DefaultValue = defaultValue });
            return defaultValue;
        }

        if (mValues.TryGetValue(name, out var value) && value != null)
        {
            try { return convert(value); }
            catch { return defaultValue; }
        }
        return defaultValue;
    }
}
