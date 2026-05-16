namespace Todos.Web.Exceptions;

/// <summary>Thrown when the Notifications service does not respond in time.</summary>
public class NotificationsTimeoutException : Exception
{
    public NotificationsTimeoutException()
        : base("The Notifications service did not respond in time.") { }
}
