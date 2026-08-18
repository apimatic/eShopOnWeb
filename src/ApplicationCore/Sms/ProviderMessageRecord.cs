using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// One message as the provider itself records it, used to reconcile the provider's record
/// against what eShop believes it sent.
/// </summary>
/// <param name="ProviderMessageId">The provider's identifier for the message (Twilio Message SID).</param>
/// <param name="To">The destination number, as the provider records it.</param>
/// <param name="From">The sending number, as the provider records it.</param>
/// <param name="Status">The provider's delivery status.</param>
/// <param name="ErrorCode">The provider's error code, when present.</param>
/// <param name="DateSent">When the provider sent the message, when present.</param>
public record ProviderMessageRecord(
    string ProviderMessageId,
    string? To,
    string? From,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateSent);
