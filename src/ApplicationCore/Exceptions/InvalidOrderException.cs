using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an order request is malformed (no items, bad quantity, unknown catalog item). Surfaced as HTTP 400.</summary>
public class InvalidOrderException : Exception
{
    public InvalidOrderException(string message) : base(message) { }
}
