using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested resource does not exist, or does not belong to the caller
/// (ownership mismatches surface as 404 to avoid leaking existence).
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
