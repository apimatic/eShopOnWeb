using Microsoft.eShopWeb.SquareCheck.SquareAccess;
using Square.Models;
using Xunit;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

public sealed class AddressFormatterTests
{
    [Fact]
    public void NoAddress_IsNull()
    {
        Assert.Null(AddressFormatter.Format(null));
        Assert.Null(AddressFormatter.Format(new Address()));
    }

    [Fact]
    public void JoinsThePartsThatArePresent()
    {
        var address = new Address
        {
            AddressLine1 = "1 Main St",
            AddressLine2 = " ",
            AddressLine3 = "Suite 5",
            Locality = "Springfield",
            PostalCode = "62701",
        };

        Assert.Equal("1 Main St, Suite 5, Springfield 62701", AddressFormatter.Format(address));
    }
}
