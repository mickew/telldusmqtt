using System.ComponentModel.DataAnnotations;

namespace telldusmqtt.Models;

internal class TelldusSettings
{
    public const string Section = "Telldus";

    [Required]
    [MinLength(1, ErrorMessage = "Host cannot be empty.")]
    [RegularExpression(@"\S+", ErrorMessage = "Host cannot be whitespace.")]
    public string Host { get; set; } = string.Empty;

    [Required]
    [MinLength(1, ErrorMessage = "AppName cannot be empty.")]
    [RegularExpression(@"\S+", ErrorMessage = "AppName cannot be whitespace.")]
    public string AppName { get; set; } = string.Empty;

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
