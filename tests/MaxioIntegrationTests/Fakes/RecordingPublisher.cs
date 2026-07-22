using MediatR;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Captures the in-process notifications the subscription service publishes, and can be made to
/// fail so the best-effort guarantee (plan.md §2.5) is actually tested rather than assumed.
/// </summary>
internal sealed class RecordingPublisher : IPublisher
{
    internal List<INotification> Published { get; } = new();

    /// <summary>When set, every publish throws — standing in for a handler that blows up.</summary>
    internal Exception? Failure { get; set; }

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        if (notification is INotification typed)
        {
            Published.Add(typed);
        }

        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Published.Add(notification);
        return Task.CompletedTask;
    }

    internal T Single<T>() where T : INotification => Published.OfType<T>().Single();
}
