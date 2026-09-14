using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

public class SubscriptionPersistenceModelTests
{
    [Fact]
    public void ConfiguresApplicationAndProviderIdempotencyKeysAsUnique()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new CatalogContext(options);

        var enrollment = context.Model.FindEntityType(typeof(SubscriptionEnrollment))!;
        Assert.True(enrollment.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId", "ProductHandle" })).IsUnique);
        Assert.True(enrollment.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "SubscriptionReference" })).IsUnique);

        var customer = context.Model.FindEntityType(typeof(MaxioCustomerLink))!;
        Assert.True(customer.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "UserId" })).IsUnique);
        Assert.True(customer.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "CustomerReference" })).IsUnique);
    }

    [Fact]
    public void EnrollmentStateTransitionsPreserveTheClaim()
    {
        var enrollment = new SubscriptionEnrollment("user-id", "plan-handle", "provider-reference");

        enrollment.MarkUnknown();
        Assert.Equal(EnrollmentStatus.Unknown, enrollment.Status);

        enrollment.MarkSucceeded(42);
        Assert.Equal(EnrollmentStatus.Succeeded, enrollment.Status);
        Assert.Equal(42, enrollment.MaxioSubscriptionId);
        Assert.Equal("provider-reference", enrollment.SubscriptionReference);
    }
}
