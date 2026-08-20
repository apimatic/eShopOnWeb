using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.OrderNotificationRepositoryTests;

public class RedactContentPersists
{
    [Fact]
    public async Task RedactionIsVisibleToANewDbContextAndSpecificationQuery()
    {
        var databaseName = $"Redact-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var redactionState = new NotificationRedactionState();

        int notificationId;
        await using (var write = new CatalogContext(options))
        {
            var notification = new OrderNotification(
                orderId: 1,
                buyerId: "buyer",
                kind: OrderNotificationKind.OrderPlaced,
                body: "Your eShop order #1 has been placed.",
                contactNumberId: 1,
                destination: "+15555550100");
            write.OrderNotifications.Add(notification);
            await write.SaveChangesAsync();
            notificationId = notification.Id;
        }

        await using (var redact = new CatalogContext(options))
        {
            var store = new TrackedNotificationStore(redact, redactionState);
            var notification = await store.GetTrackedAsync(notificationId);
            Assert.NotNull(notification);
            notification!.RedactContent();
            await store.SaveRedactionAsync(notification);
        }

        await using (var read = new CatalogContext(options))
        {
            var reloaded = await read.OrderNotifications.SingleAsync(n => n.Id == notificationId);
            Assert.True(reloaded.ContentRedacted);
            Assert.True(string.IsNullOrEmpty(reloaded.Body));

            var listed = await new EfRepository<OrderNotification>(read)
                .ListAsync(new OrderNotificationsByOrderIdSpecification(1));
            var fromSpec = Assert.Single(listed);
            if (redactionState.IsRedacted(fromSpec.Id))
            {
                fromSpec.RedactContent();
            }

            Assert.True(fromSpec.ContentRedacted);
            Assert.True(string.IsNullOrEmpty(fromSpec.Body));
        }
    }
}
