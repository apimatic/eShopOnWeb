using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested resource does not exist, or does not belong to the caller (kept indistinguishable on purpose).</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>The operation is not valid for the resource's current state (e.g. fulfilling an order that was never paid).</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>A request was malformed or violated a business rule the caller can fix (e.g. refund exceeds captured amount).</summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
