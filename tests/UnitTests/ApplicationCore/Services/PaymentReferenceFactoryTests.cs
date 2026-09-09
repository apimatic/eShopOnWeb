using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentReferenceFactoryTests
{
    [Fact]
    public void CustomIdRoundTripsToOrderId()
    {
        var factory = new PaymentReferenceFactory();
        var customId = factory.CustomId(42);

        Assert.StartsWith(factory.RunId + "-", customId);
        Assert.Equal(42, factory.TryGetOrderId(customId));
    }

    [Fact]
    public void ReferencesFromAnotherRunAreNotMatched()
    {
        var runA = new PaymentReferenceFactory();
        var runB = new PaymentReferenceFactory();

        // runA must not claim runB's transactions (prevents cross-run false matches in a shared account).
        Assert.Null(runA.TryGetOrderId(runB.CustomId(42)));
        Assert.Null(runA.TryGetOrderId("42"));   // a legacy/bare order id from a prior run
        Assert.Null(runA.TryGetOrderId(null));
    }

    [Fact]
    public void DeterministicKeysAreStablePerInputButNamespacedByRun()
    {
        var factory = new PaymentReferenceFactory();

        Assert.Equal(factory.CaptureRequestId(7), factory.CaptureRequestId(7));
        Assert.NotEqual(factory.CaptureRequestId(7), factory.CaptureRequestId(8));
        Assert.Equal(factory.RefundRequestId("abc"), factory.RefundRequestId("abc"));
        Assert.Contains(factory.RunId, factory.CaptureRequestId(7));
        Assert.Contains(factory.RunId, factory.RefundRequestId("abc"));

        // Authorize keys are unique per attempt (allow retry after a decline with a different card).
        Assert.NotEqual(factory.AuthorizeRequestId(), factory.AuthorizeRequestId());
    }
}
