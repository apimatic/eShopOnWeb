using System;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalException : Exception
{
    public int HttpStatusCode { get; }

    public PayPalException(string message, int httpStatusCode = 502, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatusCode = httpStatusCode;
    }
}

public class PayPalAuthorizationExpiredException : PayPalException
{
    public string AuthorizationId { get; }

    public PayPalAuthorizationExpiredException(string authorizationId)
        : base($"Authorization {authorizationId} has expired.", 422)
    {
        AuthorizationId = authorizationId;
    }
}
