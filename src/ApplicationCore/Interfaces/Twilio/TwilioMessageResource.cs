using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;

/// <summary>
/// The subset of the provider's Message resource this integration reads back. Mirrors the fields
/// the provider owns for a message: its identifier, current status, delivery error and timings.
/// </summary>
public record TwilioMessageResource(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
