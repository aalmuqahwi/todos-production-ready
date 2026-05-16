namespace Todos.Web.Exceptions;

/// <summary>Thrown when the Notifications service circuit breaker is open.</summary>
public class NotificationsUnavailableException : Exception
{
    public NotificationsUnavailableException()
        : base("The Notifications service is temporarily unavailable.") { }
}
