using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Masks a destination number for operator-facing output, showing only the last few digits so a
/// shopper's full number is not exposed beyond the endpoints that return it to the shopper themselves.
/// </summary>
public static class PhoneMask
{
    public static string Mask(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return "unknown";
        }

        var digits = number.Where(char.IsDigit).ToArray();
        if (digits.Length <= 4)
        {
            return "****";
        }

        var last4 = new string(digits[^4..]);
        return $"***{last4}";
    }
}
