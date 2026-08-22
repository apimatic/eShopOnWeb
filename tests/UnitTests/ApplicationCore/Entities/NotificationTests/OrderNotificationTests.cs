using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.NotificationTests;

public class OrderNotificationTests
{
    [Fact]
    public void FollowUpWithScheduledStatusIsCancellable()
    {
        var notification = Create(NotificationKind.DeliveryFollowUp, "SM123", "scheduled");
        Assert.True(notification.IsCancellableFollowUp());
    }

    [Fact]
    public void FollowUpWithoutSidIsNotCancellable()
    {
        var notification = Create(NotificationKind.DeliveryFollowUp, null, "scheduled");
        Assert.False(notification.IsCancellableFollowUp());
    }

    [Fact]
    public void DispatchedMessageIsNotCancellableFollowUp()
    {
        var notification = Create(NotificationKind.OrderDispatched, "SM123", "queued");
        Assert.False(notification.IsCancellableFollowUp());
    }

    [Fact]
    public void EmptyProviderBodyMarksContentRedacted()
    {
        var notification = Create(NotificationKind.OrderPlaced, "SM123", "delivered");
        notification.ApplyProviderOutcome("delivered", null, null, string.Empty);
        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.Body);
        Assert.Equal("SM123", notification.ProviderMessageSid);
        Assert.Equal("delivered", notification.ProviderStatus);
    }

    [Fact]
    public void RedactClearsBodyAndKeepsSid()
    {
        var notification = Create(NotificationKind.OrderPlaced, "SM123", "delivered");
        notification.RedactContent();
        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.Body);
        Assert.Equal("SM123", notification.ProviderMessageSid);
        Assert.Equal("delivered", notification.ProviderStatus);
    }

    [Theory]
    [InlineData(NotificationKind.OrderPlaced, 42, "order #42 has been placed")]
    [InlineData(NotificationKind.OrderDispatched, 7, "on its way")]
    [InlineData(NotificationKind.DeliveryFollowUp, 7, "How did delivery")]
    [InlineData(NotificationKind.OrderCancelled, 7, "has been cancelled")]
    public void BodyTemplatesDescribeTheEvent(NotificationKind kind, int orderId, string expectedFragment)
    {
        var body = OrderNotificationService.BuildBody(kind, orderId);
        Assert.Contains(expectedFragment, body);
    }

    private static OrderNotification Create(NotificationKind kind, string? sid, string status)
        => new(1, "buyer", kind, "+15555550100", "hello", sid, status, null, null, null);
}
