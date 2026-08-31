using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a request conflicts with the current state of a domain object (maps to HTTP 409).
/// </summary>
public class DomainConflictException : Exception
{
    public DomainConflictException(string message) : base(message)
    {
    }
}
