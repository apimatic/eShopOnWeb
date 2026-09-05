using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio rejects a request as invalid (e.g. an unknown plan handle). Carries
/// caller-safe messages Maxio itself returned, distinct from <see cref="MaxioIntegrationException"/>.
/// </summary>
public class MaxioValidationException : Exception
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public MaxioValidationException(IReadOnlyList<string> validationErrors)
        : base(string.Join(" ", validationErrors))
    {
        ValidationErrors = validationErrors;
    }

    public MaxioValidationException(string validationError)
        : this(new[] { validationError })
    {
    }
}
