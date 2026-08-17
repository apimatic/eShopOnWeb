using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>Result of asking the provider to validate and canonicalise a phone number.</summary>
public record PhoneLookupResult(
    bool Valid,
    string? PhoneNumber,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

/// <summary>A request to create one outbound message.</summary>
public record SmsSendCommand(string To, string Body, DateTimeOffset? SendAt = null);

/// <summary>
/// Outcome of a create-message call. <see cref="Accepted"/> is true when the provider accepted the
/// request (HTTP 201) and returned a <see cref="Sid"/> — which says nothing about delivery. It is
/// false only when the create call itself failed and no message resource was created.
/// </summary>
public record SmsSendResult(
    bool Accepted,
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent = null);

/// <summary>The provider's authoritative record of a single message, as read back.</summary>
public record SmsMessageState(
    string Sid,
    string? To,
    string? From,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    string? Body);
