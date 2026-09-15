using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper tries to see or act on data that belongs to someone else (another
/// shopper's order or saved card). Surfaces to the caller as a 403 — deliberately the same
/// response whether or not the resource exists, so ownership is not leaked.
/// </summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message)
    {
    }
}
