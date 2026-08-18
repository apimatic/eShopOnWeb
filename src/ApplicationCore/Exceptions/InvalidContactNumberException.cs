using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a number the provider does not consider a usable destination is registered.</summary>
public class InvalidContactNumberException : Exception
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public InvalidContactNumberException(IReadOnlyList<string> validationErrors)
        : base("The phone number is not a usable destination.")
    {
        ValidationErrors = validationErrors;
    }
}
