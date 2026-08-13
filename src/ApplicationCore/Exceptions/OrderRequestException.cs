using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a place-order request is not something the catalog can fulfil (no lines, an unknown
/// catalog item, or a non-positive quantity).</summary>
public class OrderRequestException : Exception
{
    public OrderRequestException(string message) : base(message) { }
}
