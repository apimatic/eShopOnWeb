using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentIdempotency
{
    [Fact]
    public void ReauthorizationRetriesReuseKeyAndLaterRenewalGetsNextSequence()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.InitializePayment("USD");

        var firstAttempt = order.StartOrResumeReauthorization();
        var retry = order.StartOrResumeReauthorization();
        order.CompleteReauthorization();
        var laterRenewal = order.StartOrResumeReauthorization();

        Assert.Equal(firstAttempt, retry);
        Assert.EndsWith("-reauthorize-1", firstAttempt);
        Assert.EndsWith("-reauthorize-2", laterRenewal);
        Assert.Equal(2, order.ReauthorizationSequence);
    }
}
