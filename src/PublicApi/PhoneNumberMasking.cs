namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Masks a destination number for display in operator-facing output (e.g. reconciliation), so a
/// shopper's full number is not exposed. The provider SID remains the real correlation key.
/// </summary>
public static class PhoneNumberMasking
{
    public static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
            return number;

        const int visible = 4;
        if (number.Length <= visible)
            return new string('•', number.Length);

        var last = number.Substring(number.Length - visible);
        return new string('•', number.Length - visible) + last;
    }
}
