/// <summary>
/// Пути SignalR-хабов. Менять только здесь — не хардкодить строки в коде.
/// Должны совпадать с app.MapHub(...) в серверном Program.cs.
/// </summary>
public static class HubPaths
{
    public const string LOCATION = "/hubs/location";

    /// <summary>Пока не используется клиентом — заведён под чат (Фаза 2), чтобы путь
    /// не плодился по коду отдельной строкой, когда до чата дойдёт очередь.</summary>
    public const string CHAT = "/hubs/chat";
}
