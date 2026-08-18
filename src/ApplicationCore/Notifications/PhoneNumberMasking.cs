namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Masks a phone number for display, keeping a leading '+' and the last two digits. Used so numbers
/// are never exposed wholesale in operator-facing views or logs.
/// </summary>
public static class PhoneNumberMasking
{
    public static string? Mask(string? e164)
    {
        if (string.IsNullOrEmpty(e164))
        {
            return e164;
        }

        var hasPlus = e164[0] == '+';
        var digits = hasPlus ? e164.Substring(1) : e164;
        var prefix = hasPlus ? "+" : string.Empty;

        if (digits.Length <= 2)
        {
            return prefix + new string('•', digits.Length);
        }

        var visible = digits.Substring(digits.Length - 2);
        return prefix + new string('•', digits.Length - 2) + visible;
    }
}
