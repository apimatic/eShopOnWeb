using Square.Models;
using Square.Models.Enums;
using SquareCheck;

namespace SquareCheckTests;

public class AccountDisplayTests
{
    [Fact]
    public void Print_shows_merchant_business_name_and_id()
    {
        var merchant = new Merchant
        {
            BusinessName = "Test Bakery",
            Id = "M001",
            Country = Country.Zz,
        };

        var sw = new StringWriter();
        AccountDisplay.Print(merchant, [], sw);

        var output = sw.ToString();
        Assert.Contains("Test Bakery", output);
        Assert.Contains("M001", output);
    }

    [Fact]
    public void Print_shows_location_name_status_and_address()
    {
        var merchant = new Merchant { BusinessName = "B", Id = "M", Country = Country.Zz };
        var location = new Location
        {
            Name = "Main Street",
            Status = LocationStatus.Active,
            Address = new Address
            {
                AddressLine1 = "123 Main St",
                Locality = "Springfield",
                AdministrativeDistrictLevel1 = "IL",
                PostalCode = "62701",
            },
        };

        var sw = new StringWriter();
        AccountDisplay.Print(merchant, [location], sw);

        var output = sw.ToString();
        Assert.Contains("Main Street", output);
        Assert.Contains("ACTIVE", output);
        Assert.Contains("123 Main St", output);
        Assert.Contains("Springfield", output);
    }

    [Fact]
    public void Print_shows_no_locations_message_when_list_is_empty()
    {
        var merchant = new Merchant { BusinessName = "B", Id = "M", Country = Country.Zz };

        var sw = new StringWriter();
        AccountDisplay.Print(merchant, [], sw);

        Assert.Contains("No locations", sw.ToString());
    }

    [Fact]
    public void Print_handles_null_merchant_gracefully()
    {
        var sw = new StringWriter();
        AccountDisplay.Print(null, null, sw);

        Assert.Contains("(unknown)", sw.ToString());
    }

    [Fact]
    public void FormatAddress_combines_all_parts()
    {
        var addr = new Address
        {
            AddressLine1 = "10 Downing St",
            Locality = "London",
            PostalCode = "SW1A 2AA",
        };

        var result = AccountDisplay.FormatAddress(addr);

        Assert.Contains("10 Downing St", result);
        Assert.Contains("London", result);
        Assert.Contains("SW1A 2AA", result);
    }

    [Fact]
    public void FormatAddress_returns_null_for_empty_address()
    {
        var result = AccountDisplay.FormatAddress(new Address());
        Assert.Null(result);
    }
}
