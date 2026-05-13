using System.ComponentModel.DataAnnotations;

namespace Todos.Web.Options;

/// <summary>Options for the Notifications downstream service.</summary>
public class NotificationsOptions
{
    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 5;
}
