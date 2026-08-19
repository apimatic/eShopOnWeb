using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Shared helpers for the SMS-notification endpoints.</summary>
public static class NotificationEndpointHelpers
{
    /// <summary>
    /// The caller's identity (username/email), which is the buyer id the order/contact/notification
    /// data is scoped by. Read from the JWT's Name claim.
    /// </summary>
    public static string? GetBuyerId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name);

    /// <summary>
    /// A masked view of a phone number for API responses (e.g. "•••••••1234"), so a full
    /// number is not needlessly echoed. The last four digits are preserved for recognition.
    /// </summary>
    public static string MaskPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return string.Empty;
        if (phoneNumber.Length <= 4)
            return new string('•', phoneNumber.Length);
        return new string('•', phoneNumber.Length - 4) + phoneNumber[^4..];
    }
}
