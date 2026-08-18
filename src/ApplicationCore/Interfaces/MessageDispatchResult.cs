namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// What the provider reported for a message it accepted: its identifier, its current delivery status, and any
/// error detail it attached.
/// </summary>
public class MessageDispatchResult
{
    public MessageDispatchResult(string providerMessageSid, string status, int? errorCode = null, string? errorMessage = null)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>The provider's identifier (SID) for the message.</summary>
    public string ProviderMessageSid { get; }

    /// <summary>The provider's current status for the message (its wire value).</summary>
    public string Status { get; }

    public int? ErrorCode { get; }
    public string? ErrorMessage { get; }
}
