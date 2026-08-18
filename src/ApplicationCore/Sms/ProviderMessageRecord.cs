using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// One message as the provider itself records it, used to reconcile the provider's ledger against
/// what eShop believes it sent.
/// </summary>
public class ProviderMessageRecord
{
    public required string MessageSid { get; init; }
    public string? Status { get; init; }
    public string? From { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public int? ErrorCode { get; init; }
}
