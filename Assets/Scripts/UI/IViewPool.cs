using UnityEngine;

/// <summary>
/// Пул переиспользуемых view-компонентов для списков с полной пересборкой при каждом
/// изменении данных (см. ChatPresenter.DisplayedMessages, в перспективе — Mobs/Players).
/// Не завязан на конкретный тип данных — сам pool не знает, чем заполняется view;
/// вызывающий код делает Get() → Setup(data, ...) → и т.д.
/// </summary>
public interface IViewPool<TView> where TView : Component
{
    /// <summary>Достаёт view из пула — переиспользует свободный экземпляр или создаёт новый.</summary>
    TView Get();

    /// <summary>Возвращает конкретный view в пул (деактивирует, помечает свободным).</summary>
    void Return(TView view);

    /// <summary>Возвращает ВСЕ выданные view в пул разом — типичный кейс: полная
    /// пересборка списка (Clear + заново Get() на каждый элемент).</summary>
    void ReturnAll();
}
