using Microsoft.eShopWeb.SquareCheck.SquareAccess;
using Square.Servers;

namespace Microsoft.eShopWeb.SquareCheck;

public static class AccountPrinter
{
    public static void Print(
        TextWriter output,
        ServerEnvironment environment,
        MerchantSummary merchant,
        IReadOnlyList<LocationSummary> locations)
    {
        output.WriteLine();
        output.WriteLine($"Connected to Square ({environment.Value}).");
        output.WriteLine($"Business:    {Text(merchant.BusinessName, "(no business name)")}");
        output.WriteLine($"Merchant id: {Text(merchant.Id, "(none)")}");
        output.WriteLine();
        output.WriteLine($"Locations ({locations.Count}):");
        if (locations.Count == 0)
        {
            output.WriteLine("  (none)");
        }

        foreach (var location in locations)
        {
            output.WriteLine($"  - {Text(location.Name, "(unnamed location)")}");
            output.WriteLine($"      Status:  {Text(location.Status, "UNKNOWN")}");
            output.WriteLine($"      Address: {Text(location.Address, "(no address)")}");
        }
    }

    /// <summary>
    /// Values come from Square: strip control characters so nothing can drive the terminal.
    /// </summary>
    private static string Text(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : FailureClassifier.OneLine(value);
}
