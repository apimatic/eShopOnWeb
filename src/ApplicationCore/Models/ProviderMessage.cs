namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The provider-owned state of a single message, as reported by the messaging provider.
/// </summary>
/// <param name="Sid">The provider's message identifier.</param>
/// <param name="Status">The provider's delivery outcome (wire value, e.g. queued/sent/delivered/undelivered/failed/scheduled/canceled).</param>
/// <param name="ErrorCode">Provider error code, when the message failed.</param>
/// <param name="ErrorMessage">Provider error message, when the message failed.</param>
/// <param name="From">Sending number (provider record).</param>
/// <param name="To">Destination number (provider record).</param>
/// <param name="DateSent">When the provider sent the message (provider's own representation).</param>
public record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    string? DateSent);
