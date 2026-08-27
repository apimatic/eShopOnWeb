using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the messaging provider does not consider a number a usable destination.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(IReadOnlyList<string> validationErrors)
        : base("The phone number is not a usable destination.")
    {
        ValidationErrors = validationErrors;
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}
