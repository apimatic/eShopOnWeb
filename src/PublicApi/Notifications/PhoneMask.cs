namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// Masks a phone number for display in API responses, keeping only the last few digits visible. The
/// full number is never needed by a caller (operators act by notification id, not by number).
/// </summary>
public static class PhoneMask
{
    public static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return number;

        const int visible = 4;
        if (number.Length <= visible)
            return new string('*', number.Length);

        var suffix = number[^visible..];
        var prefix = number[0] == '+' ? "+" : string.Empty;
        return prefix + new string('*', number.Length - visible - prefix.Length) + suffix;
    }
}
