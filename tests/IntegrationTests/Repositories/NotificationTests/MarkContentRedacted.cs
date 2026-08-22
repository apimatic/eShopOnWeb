using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Repositories.NotificationTests;

public class MarkContentRedacted
{
    [Fact]
    public async Task PersistsFlagAcrossNewContextWithSharedDatabaseName()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("RedactShared")
            .Options;

        int id;
        await using (var write = new CatalogContext(options))
        {
            var notification = new OrderNotification(1, "buyer@test.com", NotificationKind.OrderPlaced, "hello", "+15551212");
            write.OrderNotifications.Add(notification);
            await write.SaveChangesAsync();
            id = notification.Id;
            Assert.True(id > 0);
        }

        await using (var mutate = new CatalogContext(options))
        {
            var persistence = new NotificationPersistence(mutate);
            await persistence.MarkContentRedactedAsync(id, default);
        }

        await using (var read = new CatalogContext(options))
        {
            var loaded = await read.OrderNotifications.AsNoTracking().SingleAsync(n => n.Id == id);
            Assert.True(loaded.ContentRedacted);
            Assert.Equal(string.Empty, loaded.Body);
        }
    }

    [Fact]
    public async Task PersistsWhenEntityAlreadyTrackedByGetById()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("RedactTracked")
            .Options;

        await using var db = new CatalogContext(options);
        var notification = new OrderNotification(1, "buyer@test.com", NotificationKind.OrderPlaced, "hello", "+15551212");
        db.OrderNotifications.Add(notification);
        await db.SaveChangesAsync();

        var repo = new EfRepository<OrderNotification>(db);
        var loaded = await repo.GetByIdAsync(notification.Id);
        Assert.NotNull(loaded);
        Assert.False(loaded!.ContentRedacted);

        var persistence = new NotificationPersistence(db);
        await persistence.MarkContentRedactedAsync(notification.Id, default);

        db.ChangeTracker.Clear();
        var again = await db.OrderNotifications.AsNoTracking().SingleAsync(n => n.Id == notification.Id);
        Assert.True(again.ContentRedacted);
    }
}
