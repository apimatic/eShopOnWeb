using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusableContactNumberException : Exception
{
    public UnusableContactNumberException(string message, IReadOnlyList<string>? validationErrors = null)
        : base(message)
    {
        ValidationErrors = validationErrors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}
