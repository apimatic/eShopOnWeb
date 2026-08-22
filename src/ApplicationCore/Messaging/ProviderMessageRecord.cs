using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed record ProviderMessageRecord(
    string Sid,
    string? Status,
    DateTimeOffset? DateSent,
    string? DateSentRaw,
    string? From,
    string? To,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? Direction);
