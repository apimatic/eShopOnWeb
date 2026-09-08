using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioBillingException : Exception
{
    public int? StatusCode { get; }

    public MaxioBillingException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
