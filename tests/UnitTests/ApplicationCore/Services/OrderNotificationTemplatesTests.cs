using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationTemplatesTests
{
    [Theory]
    [InlineData(NotificationKind.OrderPlaced, "placed")]
    [InlineData(NotificationKind.OrderDispatched, "on its way")]
    [InlineData(NotificationKind.DeliveryFollowUp, "delivery")]
    [InlineData(NotificationKind.OrderCancelled, "cancelled")]
    public void BodyDescribesTheOrderEvent(NotificationKind kind, string expectedFragment)
    {
        var body = OrderNotificationTemplates.For(kind, 42);

        Assert.Contains("#42", body);
        Assert.Contains(expectedFragment, body, StringComparison.OrdinalIgnoreCase);
    }
}
