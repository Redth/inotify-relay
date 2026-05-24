using System.Threading.Channels;
using InotifyRelay.Core.Events;

namespace InotifyRelay.Core.Pipeline;

public sealed record DeliveryWork(
    long? EventLogId,
    FileSystemChange Change,
    RuleSnapshot Rule,
    TargetBindingSnapshot Binding);

public sealed class DeliveryQueue
{
    private readonly Channel<DeliveryWork> _channel = Channel.CreateUnbounded<DeliveryWork>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ChannelWriter<DeliveryWork> Writer => _channel.Writer;
    public ChannelReader<DeliveryWork> Reader => _channel.Reader;
}
