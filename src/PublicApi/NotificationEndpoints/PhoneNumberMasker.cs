namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Masks a destination number for display, keeping only the last four digits. Used so a number is
/// not surfaced in full through notification/reconciliation responses.
/// </summary>
public static class PhoneNumberMasker
{
    public static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return number;

        if (number.Length <= 4)
            return new string('*', number.Length);

        var lastFour = number[^4..];
        return new string('*', number.Length - 4) + lastFour;
    }
}
