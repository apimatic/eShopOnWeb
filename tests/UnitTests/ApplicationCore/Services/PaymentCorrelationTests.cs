using Microsoft.eShopWeb.ApplicationCore.Services;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentCorrelationTests
{
    [Fact]
    public void OrderTokenIsNamespacedSoItDoesNotCollideWithArbitraryValues()
    {
        var token = PaymentCorrelation.OrderToken(42);
        Assert.Equal("eshop-order-42", token);
    }

    [Fact]
    public void RoundTripsAnOrderId()
    {
        Assert.True(PaymentCorrelation.TryParseOrderId(PaymentCorrelation.OrderToken(7), out var id));
        Assert.Equal(7, id);
    }

    [Theory]
    [InlineData("7")]          // bare id must not match — this is exactly the false-positive we avoid
    [InlineData("order-7")]
    [InlineData(null)]
    [InlineData("")]
    public void RejectsTokensThatAreNotOurNamespacedForm(string? value)
    {
        Assert.False(PaymentCorrelation.TryParseOrderId(value, out _));
    }
}
