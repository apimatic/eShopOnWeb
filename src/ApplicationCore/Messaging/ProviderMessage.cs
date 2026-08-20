using System;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record ProviderMessage(
    string Sid,
    string? Status,
    string? Body,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent,
    string? DateCreated);
