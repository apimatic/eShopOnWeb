using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested resource does not exist, or does not belong to the caller. Mapped to HTTP 404.
/// (Ownership failures are reported as "not found" so one shopper cannot probe another's data.)
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
