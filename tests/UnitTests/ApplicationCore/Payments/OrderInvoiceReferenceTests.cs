using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class OrderInvoiceReferenceTests
{
    [Fact]
    public void RoundTripsTheOrderId()
    {
        var invoice = OrderInvoiceReference.For(42, "abcd1234");
        Assert.True(OrderInvoiceReference.TryParse(invoice, out var orderId));
        Assert.Equal(42, orderId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SOMETHINGELSE-42")]
    [InlineData("ESHOP-notanumber-x")]
    public void RejectsUnrelatedInvoiceIds(string? invoiceId)
    {
        Assert.False(OrderInvoiceReference.TryParse(invoiceId, out _));
    }
}
