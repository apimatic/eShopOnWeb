namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>Outcome of a messaging-API call that accepts or changes a message.</summary>
public record MessagingOutcome
{
    public bool Success => FailureKind == MessagingFailureKind.None;
    public MessagingFailureKind FailureKind { get; init; } = MessagingFailureKind.None;
    public string? MessageSid { get; init; }
    public string? Status { get; init; }
    public int? ProviderStatusCode { get; init; }
    public int? ProviderErrorCode { get; init; }
    public string? ProviderErrorMessage { get; init; }

    public static MessagingOutcome Succeeded(string? messageSid, string? status) =>
        new() { MessageSid = messageSid, Status = status };

    public static MessagingOutcome Failed(MessagingFailureKind kind, int? providerStatusCode = null,
        int? providerErrorCode = null, string? providerErrorMessage = null) =>
        new()
        {
            FailureKind = kind,
            ProviderStatusCode = providerStatusCode,
            ProviderErrorCode = providerErrorCode,
            ProviderErrorMessage = providerErrorMessage
        };
}
