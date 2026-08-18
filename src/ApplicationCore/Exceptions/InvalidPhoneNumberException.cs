using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller-supplied phone number is not a usable destination, so registration is
/// rejected up front. Carries the provider's validation reasons, if any.
/// </summary>
public class InvalidPhoneNumberException : Exception
{
    public InvalidPhoneNumberException(string message, IReadOnlyList<string>? validationErrors = null)
        : base(message)
    {
        ValidationErrors = validationErrors ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> ValidationErrors { get; }
}
