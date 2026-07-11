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
///  — Trade/Private: через ChatHub — см. IChatRealtimeService.ChatMessageReceived.
/// </summary>
public sealed class ChatMessageView
{
    [JsonProperty("id")] public long Id { get; set; }

    /// <summary>lowercase: "location"/"trade"/"group"/"private". См. ChatChannelTypes.VIEW_*.</summary>
    [JsonProperty("channelType")] public string ChannelType { get; set; }

    /// <summary>Id локации (Location) / получателя (Private). null для Trade.</summary>
    [JsonProperty("channelScopeId")] public long? ChannelScopeId { get; set; }

    [JsonProperty("senderCharacterId")] public long SenderCharacterId { get; set; }
    [JsonProperty("senderNickname")] public string SenderNickname { get; set; }

    /// <summary>Текст уже отфильтрован сервером (мат замаскирован).</summary>
    [JsonProperty("text")] public string Text { get; set; }

    /// <summary>Серверное время отправки, UTC.</summary>
    [JsonProperty("sentAt")] public DateTime SentAt { get; set; }
}
