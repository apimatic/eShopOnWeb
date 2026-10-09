using Square.Models;
using Square.Models.Enums;
using SquareCheck.Display;
using Xunit;

namespace SquareCheck.Tests.Display;

public sealed class AccountPrinterTests
{
    private static AccountPrinter MakePrinter(StringWriter writer) => new(writer);

    private static Merchant MakeMerchant(string id = "MTEST1", string? name = "Test Bakery") =>
        new()
        {
            Id = id,
            BusinessName = name,
            Country = Country.Us,
        };

    // ─── PrintMerchant ────────────────────────────────────────────────────────

    [Fact]
    public void PrintMerchant_IncludesBusinessNameAndId()
    {
        using var sw = new StringWriter();
        var printer = MakePrinter(sw);

        printer.PrintMerchant(MakeMerchant("M_ID", "My Shop"));

        var output = sw.ToString();
        Assert.Contains("My Shop", output);
        Assert.Contains("M_ID", output);
    }

    [Fact]
    public void PrintMerchant_HandlesMissingName()
    {
        using var sw = new StringWriter();
        var printer = MakePrinter(sw);

        printer.PrintMerchant(MakeMerchant("M123", null));

        Assert.Contains("M123", sw.ToString());
    }

    // ─── PrintLocations ───────────────────────────────────────────────────────

    [Fact]
    public void PrintLocations_ShowsNameStatusAndAddress()
    {
        var location = new Location
        {
            Id = "LOC1",
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

        using var sw = new StringWriter();
        MakePrinter(sw).PrintLocations([location]);

        var output = sw.ToString();
        Assert.Contains("Main Street", output);
        Assert.Contains("LOC1", output);
        Assert.Contains("ACTIVE", output);
        Assert.Contains("123 Main St", output);
        Assert.Contains("Springfield", output);
        Assert.Contains("IL", output);
        Assert.Contains("62701", output);
    }

    [Fact]
    public void PrintLocations_ShowsInactiveStatus()
    {
        var location = new Location
        {
            Id = "LOC2",
            Name = "Warehouse",
            Status = LocationStatus.Inactive,
        };

        using var sw = new StringWriter();
        MakePrinter(sw).PrintLocations([location]);

        Assert.Contains("INACTIVE", sw.ToString());
    }

    [Fact]
    public void PrintLocations_HandlesNullLocations()
    {
        using var sw = new StringWriter();
        MakePrinter(sw).PrintLocations(null);

        Assert.Contains("none", sw.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintLocations_HandlesEmptyList()
    {
        using var sw = new StringWriter();
        MakePrinter(sw).PrintLocations([]);

        Assert.Contains("none", sw.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintLocations_HandlesLocationWithNoAddress()
    {
        var location = new Location { Id = "L1", Name = "Mobile", Status = LocationStatus.Active };
        using var sw = new StringWriter();
        MakePrinter(sw).PrintLocations([location]);

        var output = sw.ToString();
        Assert.Contains("Mobile", output);
        Assert.Contains("L1", output);
    }

    [Fact]
    public void PrintLocations_ShowsMultipleLocations()
    {
        var locations = new List<Location>
        {
            new() { Id = "L1", Name = "Alpha", Status = LocationStatus.Active },
            new() { Id = "L2", Name = "Beta", Status = LocationStatus.Inactive },
        };

        using var sw = new StringWriter();
        MakePrinter(sw).PrintLocations(locations);

        var output = sw.ToString();
        Assert.Contains("Alpha", output);
        Assert.Contains("Beta", output);
        Assert.Contains("(2)", output);
    }
}
