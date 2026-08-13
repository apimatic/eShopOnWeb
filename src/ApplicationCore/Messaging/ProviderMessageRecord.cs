namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// One message as the provider records it, used by the reconciliation report. The destination is
/// carried for lining records up but is never written to logs. <paramref name="DateSent"/> is the
/// provider's own date string.
/// </summary>
public record ProviderMessageRecord(
    string ProviderSid,
    string? To,
    string? From,
    string Status,
    string? DateSent);
