namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string message, string? processorDebugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProcessorDebugId = processorDebugId;
    }

    public int StatusCode { get; }
    public string? ProcessorDebugId { get; }
}
