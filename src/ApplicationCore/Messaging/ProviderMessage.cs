using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// One message as the provider knows it, used to reconcile the provider's record against eShop's.
/// </summary>
public sealed record ProviderMessage(
    string Sid,
    string? Status,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode);
