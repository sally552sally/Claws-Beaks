using System;
using TMPro;

/// <summary>
/// Расширения для Reactive&lt;T&gt; и ReadonlyReactive&lt;T&gt;.
/// Select, Combine, Format — реактивные трансформации без ручного управления подписками.
/// SetTextSource — прямая привязка TMP_Text к реактивной строке.
/// </summary>
public static class ReactiveExtensions
{
    // ─── Select ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт производное ReadonlyReactive с преобразованным значением.
    /// Автоматически обновляется при изменении источника.
    /// Время жизни производного ограничено временем жизни источника.
    /// </summary>
    public static ReadonlyReactive<TOut> Select<TIn, TOut>(
        this ReadonlyReactive<TIn> source,
        Func<TIn, TOut> converter)
    {
        var result = new Reactive<TOut>();
        result.DisposeWhenLifeEnded(source);
        source.SubscribeOnValueChanged(v => result.Value = converter(v));
        return result.Readonly;
    }

    /// <summary>Перегрузка для Reactive&lt;T&gt; вместо ReadonlyReactive&lt;T&gt;.</summary>
    public static ReadonlyReactive<TOut> Select<TIn, TOut>(
        this Reactive<TIn> source,
        Func<TIn, TOut> converter)
        => source.Readonly.Select(converter);

    // ─── Combine ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт ReadonlyReactive, значение которого зависит от двух источников.
    /// Обновляется при изменении любого из них.
    /// </summary>
    public static ReadonlyReactive<TOut> Combine<TOut, T1, T2>(
        ReadonlyReactive<T1> input1,
        ReadonlyReactive<T2> input2,
        Func<T1, T2, TOut> selector,
        ILifeScope lifeScope = null)
    {
        var result = new Reactive<TOut>();
        if (lifeScope != null) result.DisposeWhenLifeEnded(lifeScope);

        void Refresh() => result.Value = selector(input1.Value, input2.Value);

        input1.SubscribeOnValueChanged(_ => Refresh()).DisposeWhenLifeEnded(result);
        input2.SubscribeOnValueChanged(_ => Refresh(), callOnSubscribe: false).DisposeWhenLifeEnded(result);

        return result.Readonly;
    }

    /// <summary>Три источника.</summary>
    public static ReadonlyReactive<TOut> Combine<TOut, T1, T2, T3>(
        ReadonlyReactive<T1> input1,
        ReadonlyReactive<T2> input2,
        ReadonlyReactive<T3> input3,
        Func<T1, T2, T3, TOut> selector,
        ILifeScope lifeScope = null)
    {
        var result = new Reactive<TOut>();
        if (lifeScope != null) result.DisposeWhenLifeEnded(lifeScope);

        void Refresh() => result.Value = selector(input1.Value, input2.Value, input3.Value);

        input1.SubscribeOnValueChanged(_ => Refresh()).DisposeWhenLifeEnded(result);
        input2.SubscribeOnValueChanged(_ => Refresh(), false).DisposeWhenLifeEnded(result);
        input3.SubscribeOnValueChanged(_ => Refresh(), false).DisposeWhenLifeEnded(result);

        return result.Readonly;
    }

    /// <summary>Четыре источника.</summary>
    public static ReadonlyReactive<TOut> Combine<TOut, T1, T2, T3, T4>(
        ReadonlyReactive<T1> input1,
        ReadonlyReactive<T2> input2,
        ReadonlyReactive<T3> input3,
        ReadonlyReactive<T4> input4,
        Func<T1, T2, T3, T4, TOut> selector,
        ILifeScope lifeScope = null)
    {
        var result = new Reactive<TOut>();
        if (lifeScope != null) result.DisposeWhenLifeEnded(lifeScope);

        void Refresh() => result.Value = selector(input1.Value, input2.Value, input3.Value, input4.Value);

        input1.SubscribeOnValueChanged(_ => Refresh()).DisposeWhenLifeEnded(result);
        input2.SubscribeOnValueChanged(_ => Refresh(), false).DisposeWhenLifeEnded(result);
        input3.SubscribeOnValueChanged(_ => Refresh(), false).DisposeWhenLifeEnded(result);
        input4.SubscribeOnValueChanged(_ => Refresh(), false).DisposeWhenLifeEnded(result);

        return result.Readonly;
    }

    // ─── Format ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт реактивную строку из частей.
    /// Части могут быть строками или ReadonlyReactive&lt;string&gt; — тогда строка обновится при их изменении.
    /// 
    /// Пример:
    /// <code>
    /// // "Уровень: 5 / 10"
    /// var label = ReactiveExtensions.Format("Уровень: ", mLevel, " / ", mMaxLevel);
    /// mText.SetTextSource(label).DisposeWhenLifeEnded(this);
    /// </code>
    /// </summary>
    public static ReadonlyReactive<string> Format(params object[] parts)
    {
        var result = new Reactive<string>();

        void Refresh()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
                sb.Append(part?.ToString());
            result.Value = sb.ToString();
        }

        foreach (var part in parts)
        {
            if (part is ReadonlyReactive<string> reactive)
                reactive.SubscribeOnValueChanged(_ => Refresh(), callOnSubscribe: false)
                    .DisposeWhenLifeEnded(result);
            else if (part is Reactive<string> mutable)
                mutable.SubscribeOnValueChanged(_ => Refresh(), callOnSubscribe: false)
                    .DisposeWhenLifeEnded(result);
        }

        Refresh();
        return result.Readonly;
    }

    // ─── TMP_Text ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Привязывает TMP_Text к реактивной строке.
    /// Текст обновляется автоматически при изменении источника.
    /// 
    /// Паттерн использования во View:
    /// <code>
    /// mUsernameLabel.SetTextSource(mPresenter.Username).DisposeWhenLifeEnded(this);
    /// </code>
    /// </summary>
    public static IDisposable SetTextSource(this TMP_Text text, ReadonlyReactive<string> source)
    {
        if (source == null)
        {
            text.text = string.Empty;
            return new ActionDisposable(null);
        }

        return source.SubscribeOnValueChanged(value =>
        {
            // Проверка на null на случай уничтожения объекта в середине кадра
            if (text != null)
                text.text = value;
        });
    }

    /// <summary>Перегрузка для Reactive&lt;string&gt;.</summary>
    public static IDisposable SetTextSource(this TMP_Text text, Reactive<string> source)
        => text.SetTextSource(source?.Readonly);
}
