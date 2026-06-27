using Newtonsoft.Json;

/// <summary>
/// Десериализация тела ошибки от сервера.
/// Сервер всегда возвращает { "error": "..." } при 4xx/5xx.
/// </summary>
public class ApiError
{
    [JsonProperty("error")]
    public string Error { get; set; }
}
