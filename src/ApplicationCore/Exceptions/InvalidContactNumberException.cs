using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message, IReadOnlyList<string>? validationErrors = null)
        : base(message)
    {
        ValidationErrors = validationErrors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}
