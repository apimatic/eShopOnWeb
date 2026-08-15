using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Models;

public class OrderInvoiceTests
{
    [Fact]
    public void ForBuildsPrefixedUniqueInvoiceId()
    {
        Assert.Equal("eshop-order-42-nonce123", OrderInvoice.For(42, "nonce123"));
    }

    [Fact]
    public void TryGetOrderIdRoundTrips()
    {
        var invoice = OrderInvoice.For(7, "abcdef123456");
        Assert.Equal(7, OrderInvoice.TryGetOrderId(invoice));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something-else-9")]
    [InlineData("eshop-order-notanumber-x")]
    public void TryGetOrderIdReturnsNullForNonMatching(string? invoiceId)
    {
        Assert.Null(OrderInvoice.TryGetOrderId(invoiceId));
    }
}
