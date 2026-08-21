using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(IEnumerable<string>? validationErrors)
        : base(BuildMessage(validationErrors))
    {
        ValidationErrors = (validationErrors ?? Array.Empty<string>()).ToArray();
    }

    public IReadOnlyList<string> ValidationErrors { get; }

    private static string BuildMessage(IEnumerable<string>? validationErrors)
    {
        var errors = (validationErrors ?? Array.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
        if (errors.Length == 0)
        {
            return "The phone number is not a usable destination.";
        }

        return "The phone number is not a usable destination: " + string.Join(", ", errors);
    }
}
