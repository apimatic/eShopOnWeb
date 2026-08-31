using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

/// <summary>Result of asking the provider to validate and canonicalize a phone number.</summary>
public sealed record ValidatedPhoneNumber(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

/// <summary>A provider message record, projected onto app-owned types.</summary>
public sealed record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    string? Body,
    DateTimeOffset? DateSent);

/// <summary>A fully-paged list of provider messages for a date range.</summary>
public sealed record ProviderMessageList(
    IReadOnlyList<ProviderMessage> Messages,
    bool Truncated);
