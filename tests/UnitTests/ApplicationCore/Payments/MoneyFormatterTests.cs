using Microsoft.eShopWeb.ApplicationCore.Payments;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Payments;

public class MoneyFormatterTests
{
    [Theory]
    [InlineData(19.5, "19.50")]
    [InlineData(8.5, "8.50")]
    [InlineData(12, "12.00")]
    [InlineData(0.1, "0.10")]
    public void FormatsToTwoDecimalPlaces(decimal amount, string expected)
    {
        Assert.Equal(expected, MoneyFormatter.ToPayPalValue(amount));
    }

    [Fact]
    public void ParsesPayPalValue()
    {
        Assert.Equal(19.50m, MoneyFormatter.FromPayPalValue("19.50"));
    }

    [Fact]
    public void CardToStringRedactsPan()
    {
        var card = new CardPaymentDetails
        {
            Number = "4111111111111111",
            Expiry = "2027-12",
            SecurityCode = "123",
            Name = "Test"
        };

        var text = card.ToString();
        Assert.DoesNotContain("4111111111111111", text);
        Assert.DoesNotContain("123", text);
        Assert.Contains("1111", text);
    }
}
