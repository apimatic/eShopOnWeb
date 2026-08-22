using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
