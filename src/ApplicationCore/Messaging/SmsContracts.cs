using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record LookedUpPhoneNumber(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

public record SmsDispatchResult(
    bool Accepted,
    string? ProviderMessageSid,
    string Status,
    string? ErrorCode);

public record SmsMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    string? From,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? ErrorCode);
