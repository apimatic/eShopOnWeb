using Microsoft.eShopWeb.SquareCheck.SquareAccess;

namespace Microsoft.eShopWeb.SquareCheck;

public static class AccountReport
{
    public static void Write(TextWriter output, string environmentName, MerchantSummary merchant, IReadOnlyList<LocationSummary> locations)
    {
        output.WriteLine($"Connected to Square ({environmentName}).");
        output.WriteLine($"Business: {merchant.BusinessName ?? "(no business name on file)"}");
        output.WriteLine($"Merchant ID: {merchant.Id}");
        output.WriteLine();
        output.WriteLine(locations.Count == 1 ? "1 location:" : $"{locations.Count} locations:");
        foreach (var location in locations)
        {
            output.WriteLine($"  {location.Name ?? "(unnamed location)"}");
            output.WriteLine($"    Status:  {location.Status}");
            output.WriteLine($"    Address: {location.Address ?? "(no address on file)"}");
            if (!string.IsNullOrEmpty(location.Id))
            {
                output.WriteLine($"    ID:      {location.Id}");
            }
        }
    }
}
