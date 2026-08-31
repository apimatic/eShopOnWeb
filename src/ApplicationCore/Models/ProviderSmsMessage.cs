using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The provider's own record of a message, used for reconciliation.
/// </summary>
public record ProviderSmsMessage(
    string Sid,
    string? To,
    string? From,
    string? Status,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
