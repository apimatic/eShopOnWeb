using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionClaimTests
{
    [TestMethod]
    public async Task SecondClaimForSameUserIsRefusedByTheStore()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        await using var first = new CatalogContext(options);
        await using var second = new CatalogContext(options);
        first.SubscriptionClaims.Add(new SubscriptionClaim("u1", "plan", "ref", DateTime.UtcNow));
        await first.SaveChangesAsync();

        second.SubscriptionClaims.Add(new SubscriptionClaim("u1", "plan", "ref", DateTime.UtcNow));
        // the in-memory provider raises ArgumentException; relational providers raise DbUpdateException
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => second.SaveChangesAsync());
        StringAssert.Contains(ex.Message, "same key");
    }
}
