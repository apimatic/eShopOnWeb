using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The billing system refused the request (validation); nothing was created.
/// </summary>
public class BillingRequestRejectedException : Exception
{
    public BillingRequestRejectedException(string message, IReadOnlyList<string> errors, Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
