using MediatR;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Captures the in-process notifications the subscription service publishes, and can be made to
/// fail so best-effort eventing can be proven not to undo an applied billing change.
/// </summary>
public sealed class RecordingPublisher : IPublisher
{
    public List<object> Published { get; } = new();

    public bool ThrowOnPublish { get; set; }

    public Task Publish(object notification, CancellationToken cancellationToken = default) =>
        Record(notification);

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification =>
        Record(notification!);

    public bool PublishedExactlyOne<TNotification>() => Published.OfType<TNotification>().Count() == 1;

    public TNotification Single<TNotification>() => Published.OfType<TNotification>().Single();

    private Task Record(object notification)
    {
        if (ThrowOnPublish)
        {
            throw new InvalidOperationException("a notification handler blew up");
        }

        Published.Add(notification);
        return Task.CompletedTask;
    }
}
