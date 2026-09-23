using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Notifications;

public class NotificationStatusMapperTests
{
    [Theory]
    [InlineData("delivered", NotificationDeliveryStatus.Delivered)]
    [InlineData("sent", NotificationDeliveryStatus.Sent)]
    [InlineData("read", NotificationDeliveryStatus.Sent)]
    public void Maps_done_states(string provider, NotificationDeliveryStatus expected)
        => Assert.Equal(expected, NotificationStatusMapper.FromProviderStatus(provider));

    [Theory]
    [InlineData("failed", NotificationDeliveryStatus.Failed)]
    [InlineData("undelivered", NotificationDeliveryStatus.Failed)]
    [InlineData("canceled", NotificationDeliveryStatus.Cancelled)]
    public void Maps_failed_states(string provider, NotificationDeliveryStatus expected)
        => Assert.Equal(expected, NotificationStatusMapper.FromProviderStatus(provider));

    [Theory]
    [InlineData("queued")]
    [InlineData("sending")]
    [InlineData("accepted")]
    [InlineData("receiving")]
    [InlineData("partially_delivered")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("some_new_status_twilio_added")]
    public void Unknown_or_in_progress_is_never_success(string? provider)
    {
        var status = NotificationStatusMapper.FromProviderStatus(provider);
        Assert.NotEqual(NotificationDeliveryStatus.Delivered, status);
        Assert.NotEqual(NotificationDeliveryStatus.Sent, status);
        // not-yet bucket
        Assert.True(status is NotificationDeliveryStatus.Pending or NotificationDeliveryStatus.Scheduled);
    }

    [Fact]
    public void Scheduled_maps_to_scheduled()
        => Assert.Equal(NotificationDeliveryStatus.Scheduled, NotificationStatusMapper.FromProviderStatus("scheduled"));
}
