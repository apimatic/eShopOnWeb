using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The pay-by-bank features can't carry out a request. Carries the HTTP status and the reason to answer with.
/// </summary>
public class BankingException : Exception
{
    public int StatusCode { get; }

    public BankingException(int statusCode, string reason, Exception? inner = null) : base(reason, inner)
    {
        StatusCode = statusCode;
    }
}
