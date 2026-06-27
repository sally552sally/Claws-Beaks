#if DEV_BUILD
/// <summary>
/// Захардкоженные данные для автологина в дев-режиме.
/// Файл в гите — credentials от локального дев-сервера, не прод.
/// Убрать DEV_BUILD из Scripting Define Symbols перед любым релизным билдом.
/// </summary>
public static class DevCredentials
{
    public const string EMAIL    = "client.admin@test.local";
    public const string PASSWORD = "ClawsAdmin2026!";
}
#endif



