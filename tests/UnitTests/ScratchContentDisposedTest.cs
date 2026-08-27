using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace UnitTests;

public class ScratchContentDisposedTest
{
    [Fact]
    public async Task ContentDisposed_PersistsAcrossContexts()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("scratch-dispose-test")
            .Options;

        int id;
        using (var ctx = new CatalogContext(options))
        {
            var n = new OrderNotification(1, "buyer", 1, NotificationKind.OrderPlaced, "hello");
            n.MarkAccepted("SM123", "delivered");
            ctx.OrderNotifications.Add(n);
            await ctx.SaveChangesAsync();
            id = n.Id;
        }

        using (var ctx = new CatalogContext(options))
        {
            var n = await ctx.OrderNotifications.FindAsync(id);
            n!.MarkContentDisposed();
            ctx.OrderNotifications.Update(n);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new CatalogContext(options))
        {
            var n = await ctx.OrderNotifications.FindAsync(id);
            Assert.True(n!.ContentDisposed);
            Assert.Null(n.Body);
        }
    }

    [Fact]
    public async Task ContentDisposed_Persists_ViaEfRepository()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("scratch-dispose-test-repo")
            .Options;

        int id;
        using (var ctx = new CatalogContext(options))
        {
            var repo = new EfRepository<OrderNotification>(ctx);
            var n = new OrderNotification(1, "buyer", 1, NotificationKind.OrderPlaced, "hello");
            n.MarkAccepted("SM123", "delivered");
            n = await repo.AddAsync(n);
            id = n.Id;
        }

        using (var ctx = new CatalogContext(options))
        {
            var repo = new EfRepository<OrderNotification>(ctx);
            var n = await repo.GetByIdAsync(id);
            n!.MarkContentDisposed();
            await repo.UpdateAsync(n);
        }

        using (var ctx = new CatalogContext(options))
        {
            var repo = new EfRepository<OrderNotification>(ctx);
            var n = await repo.GetByIdAsync(id);
            Assert.True(n!.ContentDisposed);
            Assert.Null(n.Body);
        }
    }
}
