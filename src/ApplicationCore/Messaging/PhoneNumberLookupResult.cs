namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed record PhoneNumberLookupResult(
    bool IsUsable,
    string? CanonicalNumber,
    string? RejectionReason,
    bool ProviderUnavailable);
