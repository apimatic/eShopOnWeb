namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>What the provider returned when a message was accepted (sent or scheduled).</summary>
public class SentTextMessage
{
    public SentTextMessage(string providerMessageId, string? status)
    {
        ProviderMessageId = providerMessageId;
        Status = status;
    }

    public string ProviderMessageId { get; }
    public string? Status { get; }
}
