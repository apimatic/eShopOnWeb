namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's current view of what became of a message.
/// </summary>
/// <param name="Status">The provider's delivery status (e.g. delivered, undelivered, failed).</param>
/// <param name="ErrorCode">The provider's error code, when the message failed or was undelivered.</param>
/// <param name="ErrorMessage">A human-readable description of the error, when present.</param>
public record SmsDeliveryState(string Status, int? ErrorCode, string? ErrorMessage);
