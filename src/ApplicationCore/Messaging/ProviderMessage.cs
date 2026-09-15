namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record ProviderMessage(
    string? Sid,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? DateSent,
    string? DateCreated,
    string? DateUpdated);
