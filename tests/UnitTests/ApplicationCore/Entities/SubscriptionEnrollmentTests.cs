using Microsoft.eShopWeb.ApplicationCore.Entities;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public sealed class SubscriptionEnrollmentTests
{
    [Fact]
    public void ClaimCanBeReleasedAndRenewedAfterAnAmbiguousOutcome()
    {
        var enrollment = new SubscriptionEnrollment("user-1", "eshop-pro", "reference-1");
        var originalToken = enrollment.ClaimToken;

        Assert.True(enrollment.HasActiveClaim(DateTimeOffset.UtcNow));

        enrollment.ReleaseClaim();
        Assert.False(enrollment.HasActiveClaim(DateTimeOffset.UtcNow));

        enrollment.RenewClaim(DateTimeOffset.UtcNow);
        Assert.True(enrollment.HasActiveClaim(DateTimeOffset.UtcNow));
        Assert.NotEqual(originalToken, enrollment.ClaimToken);
    }
}
