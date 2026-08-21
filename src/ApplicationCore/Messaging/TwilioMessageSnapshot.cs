using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? From);
