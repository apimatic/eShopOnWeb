namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The provider's current record of a single message, read back or returned by an update.
/// <paramref name="DateSent"/> is the provider's own date string (may be null before it is sent).
/// </summary>
public record MessageDeliveryState(
    string ProviderSid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent);
