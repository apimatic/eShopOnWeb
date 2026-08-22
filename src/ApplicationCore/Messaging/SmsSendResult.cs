namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record SmsSendResult(
    bool Accepted,
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);
