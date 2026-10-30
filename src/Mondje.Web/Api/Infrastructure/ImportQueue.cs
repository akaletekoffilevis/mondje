using System.Threading.Channels;

namespace Mondje.Web.Api.Infrastructure;

/// <summary>In-memory queue of import batch ids to process. Unbounded so uploads never block.</summary>
public class ImportQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid id) => _channel.Writer.TryWrite(id);
}