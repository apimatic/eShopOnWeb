namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalException : Exception
{
    public string? ErrorName { get; }
    public string? DebugId { get; }
    public int HttpStatusCode { get; }

    public PayPalException(string message, string? errorName = null, string? debugId = null, int httpStatusCode = 0)
        : base(message)
    {
        ErrorName = errorName;
        DebugId = debugId;
        HttpStatusCode = httpStatusCode;
    }

    public bool IsAuthorizationExpired =>
        ErrorName == "AUTHORIZATION_ALREADY_COMPLETED" ||
        ErrorName == "AUTHORIZATION_ALREADY_CAPTURED" ||
        ErrorName == "AUTHORIZATION_EXPIRED" ||
        (ErrorName == "UNPROCESSABLE_ENTITY" && Message.Contains("expired", StringComparison.OrdinalIgnoreCase));

    public bool IsPayerActionRequired =>
        ErrorName == "PAYER_ACTION_REQUIRED";
}
