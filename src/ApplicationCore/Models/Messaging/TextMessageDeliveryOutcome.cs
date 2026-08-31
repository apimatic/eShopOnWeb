namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>The provider's current record of a single message's delivery outcome.</summary>
public class TextMessageDeliveryOutcome
{
    public TextMessageDeliveryOutcome(string providerMessageId, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageId = providerMessageId;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string ProviderMessageId { get; }
    public string? Status { get; }
    public int? ErrorCode { get; }
    public string? ErrorMessage { get; }
}
