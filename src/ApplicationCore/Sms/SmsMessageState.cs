namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>The current delivery outcome the provider holds for a single message.</summary>
public class SmsMessageState
{
    public SmsMessageState(string? status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string? Status { get; }

    public int? ErrorCode { get; }

    public string? ErrorMessage { get; }
}
