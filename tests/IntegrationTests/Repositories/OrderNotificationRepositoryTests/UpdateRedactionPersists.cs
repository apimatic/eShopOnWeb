using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.OrderNotificationRepositoryTests;

public class UpdateRedactionPersists
{
    [Fact]
    public async Task PersistsContentRedactionAcrossContexts()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "OrderNotificationRedaction")
            .Options;

        int id;
        await using (var context = new CatalogContext(options))
        {
            var repository = new EfRepository<OrderNotification>(context);
            var created = await repository.AddAsync(new OrderNotification(
                orderId: 1,
                buyerId: "buyer",
                kind: OrderNotificationKind.OrderPlaced,
                destinationPhoneNumber: "+10000000000",
                body: "order placed",
                providerMessageSid: "SMTEST",
                providerStatus: "undelivered",
                providerErrorCode: 30034,
                scheduledFor: null));
            id = created.Id;
        }

        await using (var context = new CatalogContext(options))
        {
            var repository = new EfRepository<OrderNotification>(context);
            var notification = await repository.GetByIdAsync(id);
            Assert.NotNull(notification);
            notification!.MarkContentRedacted();
            await repository.UpdateAsync(notification);
        }

        await using (var context = new CatalogContext(options))
        {
            var notification = await context.OrderNotifications.AsNoTracking().SingleAsync(n => n.Id == id);
            Assert.True(notification.ContentRedacted);
            Assert.Null(notification.Body);
        }
    }
}
