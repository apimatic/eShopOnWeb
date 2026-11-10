using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The app (or Plaid) cannot carry out a pay-by-bank request. Rendered as a 4xx/5xx with the message as the reason.
/// </summary>
public class PayByBankException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public PayByBankException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
