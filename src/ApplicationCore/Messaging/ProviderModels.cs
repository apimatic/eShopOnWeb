using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record PhoneLookupResult(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

public record OutboundSmsRequest(
    string To,
    string Body,
    string? From = null,
    string? MessagingServiceSid = null,
    DateTimeOffset? SendAt = null);

public record ProviderMessageState(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? Body,
    string? From);
