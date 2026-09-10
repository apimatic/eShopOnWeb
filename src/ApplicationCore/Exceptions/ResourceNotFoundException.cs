using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A requested resource does not exist, or does not belong to the caller. The same
/// message is used for both so existence is never leaked across shoppers. Surfaced as 404.
/// </summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}
