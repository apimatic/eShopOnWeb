using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A projection of the provider's own record of a single message — the state the
/// provider owns. Used for the response of a send, a fetch, and each row of a list.
/// Body text is intentionally excluded: eShop does not keep a copy of the content.
/// </summary>
public record ProviderMessage(
    string Sid,
    string Status,
    string? From,
    string? To,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
