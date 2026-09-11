using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested entity does not exist, or exists but does not belong to the caller.
/// Ownership failures are reported as "not found" so one shopper cannot probe for the existence
/// of another shopper's data.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
