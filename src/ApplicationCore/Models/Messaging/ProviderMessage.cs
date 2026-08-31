using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>
/// The provider's own record of a message: its identifier and current delivery outcome.
/// </summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    DateTimeOffset? DateSent);
