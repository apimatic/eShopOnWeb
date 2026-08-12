using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// Small helpers shared by the SMS-notification endpoints: identifying the caller and
/// presenting phone numbers without exposing them in full.
/// </summary>
public static class NotificationApiHelpers
{
    /// <summary>The signed-in caller's identity (their user name / email), from the token.</summary>
    public static string? GetOwnerId(this ClaimsPrincipal user) => user.Identity?.Name;

    public static bool IsAdministrator(this ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    /// <summary>
    /// A privacy-preserving rendering of a destination number for responses that an operator
    /// may read: only the last four digits are shown.
    /// </summary>
    public static string MaskNumber(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return string.Empty;
        }

        var lastFour = phoneNumber.Length <= 4 ? phoneNumber : phoneNumber.Substring(phoneNumber.Length - 4);
        return "••••" + lastFour;
    }

    /// <summary>
    /// A shopper who supplies only catalog items still gets a valid order; the shipping address
    /// is not part of the SMS-notification surface, so a fixed placeholder is used.
    /// </summary>
    public static Address DefaultShippingAddress()
        => new Address("123 Main St.", "Kent", "OH", "United States", "44240");

    /// <summary>Response projection of a notification (never carries the full destination number).</summary>
    public static NotificationView ToView(this OrderNotification n) => new NotificationView
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ErrorCode = n.ErrorCode,
        To = MaskNumber(n.ToPhoneNumber),
        IsScheduled = n.IsScheduled,
        ScheduledFor = n.ScheduledFor,
        ContentRedacted = n.ContentRedacted,
        SentAt = n.ProviderSentAt,
        CreatedAt = n.CreatedAt
    };
}

/// <summary>What was sent and what became of it. Carries the notificationId operators act on.</summary>
public class NotificationView
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ErrorCode { get; set; }
    public string To { get; set; } = string.Empty;
    public bool IsScheduled { get; set; }
    public System.DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentRedacted { get; set; }
    public System.DateTimeOffset? SentAt { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
}
