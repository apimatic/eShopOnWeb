using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when a request to this API cannot be satisfied because it does not match a
/// plan that can be subscribed to through this integration.
/// </summary>
public sealed class MaxioValidationException : Exception
{
    public MaxioValidationException(string message)
        : base(message)
    {
    }
}
