using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Notifications;

[TestClass]
public class OrderNotificationServiceTests
{
    private static Address SampleAddress() => new("1 Main St", "Town", "ON", "CA", "A1A1A1");
    private static IReadOnlyCollection<OrderLine> Lines(int catalogItemId) => new[] { new OrderLine(catalogItemId, 1) };

    private static async Task RegisterNumberAsync(NotificationTestHarness h, string buyerId, string canonical)
    {
        h.Gateway.ValidationCanonical = canonical;
        await h.ContactNumberService.RegisterAsync(buyerId, "raw", CancellationToken.None);
    }

    private async Task<List<Notification>> NotificationsFor(NotificationTestHarness h, int orderId) =>
        (await h.Notifications.ListAsync(new NotificationsByOrderSpecification(orderId), CancellationToken.None)).ToList();

    [TestMethod]
    public async Task PlaceOrder_ReturnsOrderId_AndTellsTheShopperItWasPlaced()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        var itemId = await h.AddCatalogItemAsync();

        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);

        Assert.IsTrue(orderId > 0);
        Assert.AreEqual(1, h.Gateway.Sent.Count);
        var notifications = await NotificationsFor(h, orderId);
        Assert.AreEqual(1, notifications.Count);
        Assert.AreEqual(NotificationType.OrderPlaced, notifications[0].Type);
        Assert.IsNotNull(notifications[0].ProviderMessageSid);
    }

    [TestMethod]
    public async Task PlaceOrder_WithNoNumberOnFile_SendsNothing()
    {
        var h = new NotificationTestHarness();
        var itemId = await h.AddCatalogItemAsync();

        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);

        Assert.IsTrue(orderId > 0, "The order is still placed even with no number on file.");
        Assert.AreEqual(0, h.Gateway.Sent.Count);
        Assert.AreEqual(0, (await NotificationsFor(h, orderId)).Count);
    }

    [TestMethod]
    public async Task PlaceOrder_WhenMessagingFails_StillPlacesTheOrder()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        h.Gateway.ThrowOnSend = true;
        var itemId = await h.AddCatalogItemAsync();

        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);

        Assert.IsTrue(orderId > 0);
        var notifications = await NotificationsFor(h, orderId);
        Assert.AreEqual(1, notifications.Count);
        Assert.AreEqual(DeliveryStatuses.SendFailed, notifications[0].DeliveryStatus);
        Assert.IsNull(notifications[0].ProviderMessageSid);
    }

    [TestMethod]
    public async Task PlaceOrder_WithUnknownCatalogItem_Throws()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");

        await Assert.ThrowsExceptionAsync<UnknownCatalogItemException>(() =>
            h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(9999), SampleAddress(), CancellationToken.None));
    }

    [TestMethod]
    public async Task Dispatch_QueuesADeliveryFollowUpWithTheProvider()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        var itemId = await h.AddCatalogItemAsync();
        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);

        var found = await h.OrderNotificationService.DispatchAsync(orderId, CancellationToken.None);

        Assert.IsTrue(found);
        Assert.AreEqual(1, h.Gateway.Scheduled.Count, "A follow-up must be scheduled with the provider.");
        var followUp = (await NotificationsFor(h, orderId)).Single(n => n.Type == NotificationType.DeliveryFollowUp);
        Assert.AreEqual(DeliveryStatuses.Scheduled, followUp.DeliveryStatus);
    }

    [TestMethod]
    public async Task Cancel_CallsOffTheScheduledFollowUpBeforeItSends()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        var itemId = await h.AddCatalogItemAsync();
        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);
        await h.OrderNotificationService.DispatchAsync(orderId, CancellationToken.None);
        var followUpSid = (await NotificationsFor(h, orderId)).Single(n => n.Type == NotificationType.DeliveryFollowUp).ProviderMessageSid!;

        var found = await h.OrderNotificationService.CancelAsync(orderId, CancellationToken.None);

        Assert.IsTrue(found);
        CollectionAssert.Contains(h.Gateway.Canceled, followUpSid);
        var followUp = (await NotificationsFor(h, orderId)).Single(n => n.Type == NotificationType.DeliveryFollowUp);
        Assert.AreEqual(DeliveryStatuses.Canceled, followUp.DeliveryStatus);
    }

    [TestMethod]
    public async Task Resend_UnderTheSameKey_DoesNotSendASecondMessage()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        var itemId = await h.AddCatalogItemAsync();
        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);
        var sourceId = (await NotificationsFor(h, orderId)).Single().Id;
        var sentAfterPlace = h.Gateway.Sent.Count;

        var first = await h.OrderNotificationService.ResendAsync(sourceId, "key-1", CancellationToken.None);
        var second = await h.OrderNotificationService.ResendAsync(sourceId, "key-1", CancellationToken.None);

        Assert.AreEqual(ResendStatus.Sent, first.Status);
        Assert.AreEqual(ResendStatus.ReusedIdempotent, second.Status);
        Assert.AreEqual(first.NotificationId, second.NotificationId);
        Assert.AreEqual(sentAfterPlace + 1, h.Gateway.Sent.Count, "The repeated key must not send again.");
    }

    [TestMethod]
    public async Task Resend_UnderAFreshKey_SendsAgain()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        var itemId = await h.AddCatalogItemAsync();
        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);
        var sourceId = (await NotificationsFor(h, orderId)).Single().Id;
        var sentAfterPlace = h.Gateway.Sent.Count;

        var first = await h.OrderNotificationService.ResendAsync(sourceId, "key-1", CancellationToken.None);
        var second = await h.OrderNotificationService.ResendAsync(sourceId, "key-2", CancellationToken.None);

        Assert.AreEqual(ResendStatus.Sent, first.Status);
        Assert.AreEqual(ResendStatus.Sent, second.Status);
        Assert.AreNotEqual(first.NotificationId, second.NotificationId);
        Assert.AreEqual(sentAfterPlace + 2, h.Gateway.Sent.Count);
    }

    [TestMethod]
    public async Task Resend_ForAnUnknownNotification_ReportsNotFound()
    {
        var h = new NotificationTestHarness();
        var outcome = await h.OrderNotificationService.ResendAsync(4242, "key-1", CancellationToken.None);
        Assert.AreEqual(ResendStatus.SourceNotFound, outcome.Status);
    }

    [TestMethod]
    public async Task RedactContent_DisposesTheTextLocallyAndAtTheProvider_ButKeepsTheRecord()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        var itemId = await h.AddCatalogItemAsync();
        var orderId = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);
        var notification = (await NotificationsFor(h, orderId)).Single();
        var sid = notification.ProviderMessageSid!;

        var found = await h.OrderNotificationService.RedactContentAsync(notification.Id, CancellationToken.None);

        Assert.IsTrue(found);
        CollectionAssert.Contains(h.Gateway.Redacted, sid);
        var after = (await NotificationsFor(h, orderId)).Single();
        Assert.IsTrue(after.ContentRedacted);
        Assert.IsNull(after.Body);
        Assert.IsNotNull(after.ProviderMessageSid, "The record that a message was sent must survive.");
    }

    [TestMethod]
    public async Task Reconcile_SurfacesProviderOnlyAndEShopOnlyDiscrepancies()
    {
        var h = new NotificationTestHarness();
        await RegisterNumberAsync(h, "buyerA", "+15145550123");
        var itemId = await h.AddCatalogItemAsync();
        var order1 = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);
        var order2 = await h.OrderNotificationService.PlaceOrderAsync("buyerA", Lines(itemId), SampleAddress(), CancellationToken.None);

        var sid1 = (await NotificationsFor(h, order1)).Single().ProviderMessageSid!;
        // Provider knows sid1 (matched) and an SX eShop never sent; eShop's sid2 is absent from the provider.
        h.Gateway.ProviderList.Add(new ProviderMessageRecord(sid1, "delivered", DateTimeOffset.UtcNow, null, null));
        h.Gateway.ProviderList.Add(new ProviderMessageRecord("SXexternal", "delivered", DateTimeOffset.UtcNow, null, null));

        var report = await h.OrderNotificationService.ReconcileAsync(
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), CancellationToken.None);

        Assert.AreEqual(1, report.Matched.Count);
        Assert.AreEqual(sid1, report.Matched.Single().Sid);
        Assert.IsTrue(report.ProviderOnly.Any(e => e.Sid == "SXexternal"));
        var sid2 = (await NotificationsFor(h, order2)).Single().ProviderMessageSid!;
        Assert.IsTrue(report.EShopOnly.Any(e => e.Sid == sid2));
    }
}
