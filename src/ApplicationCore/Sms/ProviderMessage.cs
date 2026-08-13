using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's view of a single message: its identifier and current delivery outcome, plus the
/// surrounding facts a caller may need to act on or reconcile it.
/// </summary>
public record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    string? Body,
    DateTimeOffset? DateSent);
