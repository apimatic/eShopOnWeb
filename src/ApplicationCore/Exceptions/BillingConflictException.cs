using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio rejects a POST/PUT as a duplicate submission (HTTP 409 uniqueness_token).
/// </summary>
public class BillingConflictException : Exception
{
    public BillingConflictException(string message) : base(message)
    {
    }
}
