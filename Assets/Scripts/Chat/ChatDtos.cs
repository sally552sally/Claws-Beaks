using System;
using Newtonsoft.Json;

// ─── POST /api/chat/send ────────────────────────────────────────────────────

/// <summary>
/// Запрос отправки сообщения. ChannelType — см. предупреждение в ChatChannelTypes про
/// регистр (PascalCase здесь, не lowercase как в ChatMessageView). Строй через
/// ChatChannelTypes.SEND_*, не пиши руками.
/// </summary>
public sealed class SendMessageRequest
{
    [JsonProperty("channelType")] public string ChannelType { get; set; }

    /// <summary>Получатель для Private. Для остальных каналов игнорируется сервером.</summary>
    [JsonProperty("recipientCharacterId")] public long? RecipientCharacterId { get; set; }

    [JsonProperty("text")] public string Text { get; set; }
}

/// <summary>Ответ на отправку — то же сообщение, что увидят остальные (сервер уже
/// отфильтровал мат, проставил Id и серверное время).</summary>
public sealed class SendMessageResponse
{
    [JsonProperty("message")] public ChatMessageView Message { get; set; }
}

// ─── Действие на кликабельном фрагменте строки ──────────────────────────────

/// <summary>
/// Действие, привязанное к системной строке: сервер отдаёт текст и подстроку, которую надо
/// сделать кликабельной, а разметку (TMP-линк) накладывает клиент. null у всех обычных
/// сообщений и у информационных системок без действия.
///
/// Kind и Outcome приходят СТРОКАМИ (см. ChatActionKinds / ChatBattleOutcomes). Отдельные
/// enum'ы здесь заводить нельзя: неизвестное значение от более нового сервера при разборе
/// в enum превратилось бы в 0 («действия нет») молча, а строка честно доедет как есть,
/// и клиент сможет её пропустить с записью в лог.
/// </summary>
public sealed class ChatMessageActionView
{
    /// <summary>Вид действия — см. ChatActionKinds.</summary>
    [JsonProperty("kind")] public string Kind { get; set; }

    /// <summary>
    /// Ровно тот фрагмент Text, который надо сделать кликабельным (сейчас всегда
    /// «[Результат боя]»). Сервер гарантирует, что фрагмент содержится в тексте: если бы
    /// не содержался, он снял бы действие целиком, а не прислал ссылку в никуда.
    /// </summary>
    [JsonProperty("linkText")] public string LinkText { get; set; }

    /// <summary>Id боевой сессии — для ChatActionKinds.BATTLE_RESULT.</summary>
    [JsonProperty("sessionId")] public long? SessionId { get; set; }

    /// <summary>Исход боя — см. ChatBattleOutcomes.</summary>
    [JsonProperty("outcome")] public string Outcome { get; set; }
}

// ─── Сообщение чата (REST-эхо И SignalR push "ChatMessage") ────────────────

/// <summary>
/// Сообщение чата. Приходит ДВУМЯ путями для собственного отправленного сообщения:
/// сразу в ответе на отправку (SendMessageResponse.Message) и следом ещё раз через
/// SignalR (сам отправитель состоит в группе локации/торговли; для Private сервер
/// explicitly шлёт эхо в личный канал отправителя). Дедуп по Id — в IChatHistoryService,
/// не здесь.
///
/// Push "ChatMessage" приходит с ДВУХ разных хабов в зависимости от канала:
///  — Location: через LocationHub (группа "location:{id}", уже используется для мобов/игроков) —
///    см. ILocationRealtimeService.ChatMessageReceived;
///  — Trade/Private/System: через ChatHub — см. IChatRealtimeService.ChatMessageReceived.
///
/// Системки доставляются ТОЛЬКО пушем (истории у каналов нет), причём адресные — в личную
/// группу персонажа, а общие рассылки — всем сразу. С точки зрения клиента разницы нет:
/// это один поток, различающийся только ChannelScopeId.
/// </summary>
public sealed class ChatMessageView
{
    [JsonProperty("id")] public long Id { get; set; }

    /// <summary>lowercase: "location"/"trade"/"group"/"private"/"system". См. ChatChannelTypes.VIEW_*.</summary>
    [JsonProperty("channelType")] public string ChannelType { get; set; }

    /// <summary>Id локации (Location) / получателя (Private, адресная системка).
    /// null для Trade и для общей системной рассылки.</summary>
    [JsonProperty("channelScopeId")] public long? ChannelScopeId { get; set; }

    /// <summary>
    /// Отправитель. NULL — системное сообщение, его прислал сервер. Именно по этому null
    /// клиент понимает, что «ответить» и «пожаловаться» для строки не существует.
    /// Стало nullable вместе с серверной колонкой sender_character_id (миграция 0005).
    /// </summary>
    [JsonProperty("senderCharacterId")] public long? SenderCharacterId { get; set; }

    /// <summary>Ник отправителя. Пусто у системных сообщений.</summary>
    [JsonProperty("senderNickname")] public string SenderNickname { get; set; }

    /// <summary>
    /// Текст уже отфильтрован сервером (мат замаскирован). У системок фильтр не применяется —
    /// их пишет сервер; префикс «Системное сообщение: » у информационных строк тоже проставлен
    /// сервером, клиент текст не досочиняет.
    /// </summary>
    [JsonProperty("text")] public string Text { get; set; }

    /// <summary>Серверное время отправки, UTC.</summary>
    [JsonProperty("sentAt")] public DateTime SentAt { get; set; }

    /// <summary>Действие на кликабельном фрагменте. null — строка не кликабельна.</summary>
    [JsonProperty("action")] public ChatMessageActionView Action { get; set; }
}
