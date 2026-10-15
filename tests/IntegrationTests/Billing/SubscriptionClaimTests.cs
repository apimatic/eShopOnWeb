using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Xunit;
using static Microsoft.eShopWeb.IntegrationTests.Billing.FakeMaxio;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Pins how the in-memory store refuses a duplicate subscription claim, which the billing service relies on.
/// </summary>
public class SubscriptionClaimTests
{
    private readonly string _database = Guid.NewGuid().ToString();

    [Fact]
    public async Task SecondClaimForTheSameUserIsRefusedByTheStore()
    {
        using var first = NewContext(_database);
        using var second = NewContext(_database);
        first.SubscriptionEnrollments.Add(new SubscriptionEnrollment("user-1", "eshop-pro", "ref-1", DateTimeOffset.UtcNow));
        second.SubscriptionEnrollments.Add(new SubscriptionEnrollment("user-1", "eshop-pro", "ref-2", DateTimeOffset.UtcNow));

        await first.SaveChangesAsync();

        // The EF Core in-memory provider raises ArgumentException (SQL Server raises DbUpdateException).
        await Assert.ThrowsAsync<ArgumentException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task OnlyOneOfTwoConcurrentTakeoversWins()
    {
        using (var seed = NewContext(_database))
        {
            var enrollment = new SubscriptionEnrollment("user-1", "eshop-pro", "ref-1", DateTimeOffset.UtcNow);
            enrollment.MarkUnknown(DateTimeOffset.UtcNow);
            seed.SubscriptionEnrollments.Add(enrollment);
            await seed.SaveChangesAsync();
        }

        using var first = NewContext(_database);
        using var second = NewContext(_database);
        var a = await first.SubscriptionEnrollments.SingleAsync();
        var b = await second.SubscriptionEnrollments.SingleAsync();
        a.ResumeAttempt(DateTimeOffset.UtcNow);
        b.ResumeAttempt(DateTimeOffset.UtcNow);

        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
