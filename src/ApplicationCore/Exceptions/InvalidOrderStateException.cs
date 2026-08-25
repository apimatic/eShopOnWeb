using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

// Thrown when a caller asks for an operation the order's current payment state does not allow
// (e.g. paying an already-authorized order, refunding beyond what was captured).
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
