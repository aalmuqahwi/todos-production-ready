using System.Threading.Channels;

namespace Todos.Notifications.Services;

public class NotificationCounter
{
    private int _count;
    private readonly Channel<bool> _channel = Channel.CreateUnbounded<bool>();

    public ChannelReader<bool> Reader => _channel.Reader;

    public void Increment()
    {
        Interlocked.Increment(ref _count);
        _channel.Writer.TryWrite(true);
    }

    public int Get() => _count;
}
