using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class SubscriptionEnrollmentModelTests
{
    [Fact]
    public void ModelHasUniqueUserPlanAndReferenceClaims()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(nameof(ModelHasUniqueUserPlanAndReferenceClaims))
            .Options;
        using var context = new CatalogContext(options);
        var entity = context.Model.FindEntityType(typeof(SubscriptionEnrollment));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(SubscriptionEnrollment.UserId), nameof(SubscriptionEnrollment.ProductHandle) }));
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(SubscriptionEnrollment.SubscriptionReference));
        Assert.True(entity.FindProperty(nameof(SubscriptionEnrollment.ConcurrencyToken))!.IsConcurrencyToken);
    }
}
