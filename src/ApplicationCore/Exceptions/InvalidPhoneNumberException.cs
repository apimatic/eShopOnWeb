using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message, IReadOnlyList<string>? validationErrors = null)
        : base(message)
    {
        ValidationErrors = validationErrors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}
