using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record TwilioMessageSnapshot(
    string Sid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);
