namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public sealed record SmsMessageSnapshot(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? To,
    string? DateCreated,
    string? DateSent,
    string? Direction);
