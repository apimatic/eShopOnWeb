using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationBehavior
{
    private static OrderNotification New() =>
        new OrderNotification(1, "buyer@example.com", NotificationType.OrderPlaced, "+15005550006", "hello");

    [Theory]
    [InlineData("delivered", NotificationDeliveryOutcome.Reached)]
    [InlineData("read", NotificationDeliveryOutcome.Reached)]
    [InlineData("undelivered", NotificationDeliveryOutcome.NotReached)]
    [InlineData("failed", NotificationDeliveryOutcome.NotReached)]
    [InlineData("scheduled", NotificationDeliveryOutcome.Scheduled)]
    [InlineData("canceled", NotificationDeliveryOutcome.Canceled)]
    [InlineData("queued", NotificationDeliveryOutcome.InFlight)]
    [InlineData("sent", NotificationDeliveryOutcome.InFlight)]
    [InlineData(OrderNotification.SendErrorStatus, NotificationDeliveryOutcome.SendError)]
    public void ClassifiesProviderStatus(string status, NotificationDeliveryOutcome expected)
    {
        Assert.Equal(expected, OrderNotification.Classify(status));
    }

    [Fact]
    public void NoStatusIsNotSent()
    {
        Assert.Equal(NotificationDeliveryOutcome.NotSent, OrderNotification.Classify(null));
    }

    [Fact]
    public void RecordAcceptedCapturesProviderStateAndOutcome()
    {
        var n = New();
        n.RecordAccepted("SM123", "delivered", null, null);
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationDeliveryOutcome.Reached, n.Outcome);
    }

    [Fact]
    public void RecordSendErrorMarksSendError()
    {
        var n = New();
        n.RecordSendError("boom");
        Assert.Equal(NotificationDeliveryOutcome.SendError, n.Outcome);
    }

    [Fact]
    public void RedactContentClearsBodyButKeepsRecord()
    {
        var n = New();
        n.RecordAccepted("SM123", "delivered", null, null);
        n.RedactContent();
        Assert.True(n.ContentRedacted);
        Assert.Null(n.Body);
        // The record and its outcome survive.
        Assert.Equal("SM123", n.ProviderMessageSid);
        Assert.Equal(NotificationDeliveryOutcome.Reached, n.Outcome);
    }
}
