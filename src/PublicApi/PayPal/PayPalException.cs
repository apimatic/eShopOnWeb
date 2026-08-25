using System;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalException : Exception
{
    public string? PayPalErrorBody { get; }
    public int? StatusCode { get; }

    public PayPalException(string message, string? errorBody = null, int? statusCode = null)
        : base(message)
    {
        PayPalErrorBody = errorBody;
        StatusCode = statusCode;
    }
}

public class PayPalChallengeRequiredException : PayPalException
{
    public PayPalChallengeRequiredException()
        : base("PayPal requires a browser-based challenge (e.g. 3DS) to complete this payment. This is not supported in the headless integration — stop and report.")
    {
    }
}
