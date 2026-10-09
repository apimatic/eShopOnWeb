using Square.Models;

namespace Microsoft.eShopWeb.SquareCheck.SquareAccess;

public static class AddressFormatter
{
    /// <summary>
    /// One-line postal address, skipping empty parts; <c>null</c> when the location has no address.
    /// </summary>
    public static string? Format(Address? address)
    {
        if (address is null)
        {
            return null;
        }

        var cityLine = string.Join(" ", new[] { address.Locality, address.AdministrativeDistrictLevel1, address.PostalCode }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        var parts = new[]
            {
                address.AddressLine1,
                address.AddressLine2,
                address.AddressLine3,
                address.Sublocality,
                cityLine,
                address.Country?.Value,
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToList();

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
