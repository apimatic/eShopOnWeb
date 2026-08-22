namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed record SmsSendResult(
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent,
    bool OutcomeUnknown)
{
    public static SmsSendResult Unknown(string? errorMessage = null) =>
        new(null, "unknown", null, errorMessage, null, true);

    public static SmsSendResult Failed(string? status, int? errorCode, string? errorMessage) =>
        new(null, status ?? "failed", errorCode, errorMessage, null, false);
}
