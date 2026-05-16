namespace Todos.Notifications.Models;

/// <summary>Payload for a notification event raised by an upstream service.</summary>
public record NotificationRequest(int TodoId, string Message);
