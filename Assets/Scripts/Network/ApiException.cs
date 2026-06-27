using System;

/// <summary>
/// Исключение при HTTP-ошибке. Содержит статус-код и текст ошибки от сервера.
/// Перехватывается в Presenter для показа пользователю.
/// </summary>
public class ApiException : Exception
{
    /// <summary>HTTP статус-код (401, 403, 409, 500 и т.д.).</summary>
    public int StatusCode { get; }

    /// <summary>Текст из поля "error" серверного ответа.</summary>
    public string ServerError { get; }

    public ApiException(int statusCode, string serverError)
        : base($"HTTP {statusCode}: {serverError}")
    {
        StatusCode = statusCode;
        ServerError = serverError;
    }
}
