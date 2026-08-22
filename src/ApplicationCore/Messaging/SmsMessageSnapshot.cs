using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record SmsMessageSnapshot(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);
