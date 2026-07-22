namespace telldusmqtt.Models;

internal class UserSettings
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTimeOffset TokenExpiration { get; set; } = DateTimeOffset.MinValue;
}
