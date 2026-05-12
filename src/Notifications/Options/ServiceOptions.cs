using System.ComponentModel.DataAnnotations;

namespace Todos.Notifications.Options;

/// <summary>Options that identify this service instance.</summary>
public class ServiceOptions
{
    [Required]
    public string Name { get; set; } = string.Empty;
}
