using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The caller's request is malformed or missing required data (e.g. neither card nor saved-card id
/// supplied, or an invalid refund amount). Maps to HTTP 400 Bad Request.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
