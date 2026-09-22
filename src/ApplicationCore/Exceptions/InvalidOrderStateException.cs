using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order lifecycle transition is not legal from the current state.</summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message) { }
}
