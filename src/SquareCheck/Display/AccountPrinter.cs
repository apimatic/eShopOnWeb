using Square.Models;

namespace SquareCheck.Display;

internal sealed class AccountPrinter
{
    private readonly TextWriter _out;

    internal AccountPrinter(TextWriter? output = null)
    {
        _out = output ?? Console.Out;
    }

    internal void Print(Merchant merchant, IReadOnlyList<Location>? locations)
    {
        PrintMerchant(merchant);
        _out.WriteLine();
        PrintLocations(locations);
    }

    internal void PrintMerchant(Merchant merchant)
    {
        var name = merchant.BusinessName ?? "(no name)";
        var id = merchant.Id ?? "(no id)";
        _out.WriteLine($"Merchant: {name}");
        _out.WriteLine($"      ID: {id}");
    }

    internal void PrintLocations(IReadOnlyList<Location>? locations)
    {
        if (locations is null || locations.Count == 0)
        {
            _out.WriteLine("Locations: (none)");
            return;
        }

        _out.WriteLine($"Locations ({locations.Count}):");

        foreach (var loc in locations)
        {
            var name = loc.Name ?? "(no name)";
            var locId = loc.Id ?? "(no id)";
            var status = loc.Status?.Value ?? "UNKNOWN";
            _out.WriteLine($"  {name} ({locId})  [{status}]");

            var address = FormatAddress(loc.Address);
            if (address is not null)
                _out.WriteLine($"    {address}");
        }
    }

    private static string? FormatAddress(Address? address)
    {
        if (address is null) return null;

        var parts = new List<string>(6);

        if (!string.IsNullOrWhiteSpace(address.AddressLine1)) parts.Add(address.AddressLine1);
        if (!string.IsNullOrWhiteSpace(address.AddressLine2)) parts.Add(address.AddressLine2);
        if (!string.IsNullOrWhiteSpace(address.AddressLine3)) parts.Add(address.AddressLine3);

        var cityLine = string.Join(", ",
            new[] { address.Locality, address.AdministrativeDistrictLevel1 }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        if (!string.IsNullOrWhiteSpace(address.PostalCode))
            cityLine = string.IsNullOrWhiteSpace(cityLine)
                ? address.PostalCode
                : $"{cityLine} {address.PostalCode}";

        if (!string.IsNullOrWhiteSpace(cityLine)) parts.Add(cityLine);
        if (address.Country is not null) parts.Add(address.Country.Value);

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }
}
