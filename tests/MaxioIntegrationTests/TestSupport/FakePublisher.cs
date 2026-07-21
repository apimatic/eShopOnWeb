using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;

/// <summary>Records every notification published by <c>SubscriptionService</c>, without a real mediator pipeline.</summary>
public class FakePublisher : IPublisher
{
    public List<object> Published { get; } = new();

    public Task Publish(object notification, CancellationToken cancellationToken = default)
    {
        Published.Add(notification);
        return Task.CompletedTask;
    }

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
    {
        Published.Add(notification!);
        return Task.CompletedTask;
    }
}
