using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingValidationException : Exception
{
    public BillingValidationException(string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Errors { get; }
}
