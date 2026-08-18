namespace Microsoft.eShopWeb.PublicApi.NotificationsFeature;

/// <summary>
/// Masks a phone number for inclusion in operator-facing responses, so the full number is not
/// exposed there. (Numbers are never logged at all.)
/// </summary>
public static class PhoneMask
{
    public static string Mask(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return string.Empty;
        }

        var digitsToShow = phoneNumber.Length <= 4 ? 0 : 4;
        var suffix = phoneNumber.Substring(phoneNumber.Length - digitsToShow);
        return digitsToShow == 0 ? "****" : $"****{suffix}";
    }
}
