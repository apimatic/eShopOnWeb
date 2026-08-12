using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Result of a Lookups v2 phone-number lookup. <see cref="PhoneNumberE164"/> is the provider's
/// canonical E.164 form and is only meaningful when <see cref="Valid"/> is true.
/// </summary>
public record PhoneNumberLookupResult(
    bool Valid,
    string? PhoneNumberE164,
    string? NationalFormat,
    string? CountryCode);

/// <summary>
/// A Twilio message resource (2010-04-01 API) as far as this integration cares about it.
/// Mirrors the fields the spec's <c>api.v2010.account.message</c> schema exposes.
/// </summary>
public record TwilioMessageResource(
    string? Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? MessagingServiceSid,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateUpdated);
