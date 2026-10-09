using Square.Models;

namespace SquareCheck;

internal static class AccountDisplay
{
    public static void Print(
        Merchant? merchant,
        IReadOnlyList<Location>? locations,
        TextWriter? output = null)
    {
        var w = output ?? Console.Out;
        w.WriteLine($"Business:    {merchant?.BusinessName ?? "(unknown)"}");
        w.WriteLine($"Merchant ID: {merchant?.Id ?? "(unknown)"}");
        w.WriteLine();

        if (locations == null || locations.Count == 0)
        {
            w.WriteLine("No locations.");
            return;
        }

        w.WriteLine($"Locations ({locations.Count}):");
        foreach (var loc in locations)
        {
            w.WriteLine($"  {loc.Name ?? "(unnamed)"}");
            w.WriteLine($"    Status:  {loc.Status?.Value ?? "(unknown)"}");
            var addr = FormatAddress(loc.Address);
            if (addr != null)
                w.WriteLine($"    Address: {addr}");
        }
    }

    internal static string? FormatAddress(Address? address)
    {
        if (address is null) return null;

        var parts = new List<string>();

        void Add(string? s) { if (!string.IsNullOrWhiteSpace(s)) parts.Add(s!); }

        Add(address.AddressLine1);
        Add(address.AddressLine2);
        Add(address.AddressLine3);

        var cityStateZip = string.Join(" ", new[]
        {
            address.Locality,
            address.AdministrativeDistrictLevel1,
            address.PostalCode,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        Add(cityStateZip);

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
