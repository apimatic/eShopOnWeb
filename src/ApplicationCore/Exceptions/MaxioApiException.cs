namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : BillingException
{
    public MaxioApiException(int statusCode, string message, string? responseBody = null)
        : base(message, statusCode)
    {
        ResponseBody = responseBody;
    }

    public string? ResponseBody { get; }
}
