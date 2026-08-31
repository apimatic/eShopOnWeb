namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The outcome of an SMS operation against the provider. A send that the provider
/// rejected (or that never reached it) is an outcome, not an exception.
/// </summary>
public record SmsSendResult(
    bool Succeeded,
    string? MessageSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage)
{
    public static SmsSendResult Accepted(string messageSid, string? status) =>
        new(true, messageSid, status, null, null);

    public static SmsSendResult Failed(string? status, int? errorCode, string? errorMessage) =>
        new(false, null, status, errorCode, errorMessage);
}
