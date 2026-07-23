using MediatR;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Captures the in-process notifications the subscription service publishes, and can be made to
/// throw so the best-effort eventing guarantee can be proved.
/// </summary>
public sealed class RecordingPublisher : IPublisher
{
    public List<INotification> Published { get; } = new();

    /// <summary>When set, every publish throws — standing in for a failing handler.</summary>
    public Exception? Failure { get; set; }

    public Task Publish(object notification, CancellationToken cancellationToken = default) =>
        Publish((INotification)notification, cancellationToken);

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

    /// <summary>The single notification of the given type, failing the test if there is not exactly one.</summary>
    public T Single<T>() where T : INotification => Published.OfType<T>().Single();
}
