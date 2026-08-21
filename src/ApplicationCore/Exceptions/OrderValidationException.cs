using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The caller's request was well-formed but semantically invalid (e.g. no order lines, an unknown catalog
/// item, or neither a card nor a saved-card id supplied). Surfaced as HTTP 400.
/// </summary>
public class OrderValidationException : Exception
{
    public OrderValidationException(string message) : base(message)
    {
    }
}
