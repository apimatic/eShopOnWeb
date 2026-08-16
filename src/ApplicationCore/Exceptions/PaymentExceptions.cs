using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Requested resource does not exist (or is not visible to the caller). Maps to 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

/// <summary>Caller tried to see or act on data that is not theirs. Maps to 403.</summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message) : base(message) { }
}

/// <summary>Request is well-formed but not valid for the current state (e.g. bad amount, wrong order state). Maps to 400.</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>
/// A payment operation could not be completed and needs an operator/shopper decision — for example an
/// authorization that can no longer be renewed. Carries a message phrased so the caller can act on it. Maps to 422.
/// </summary>
public class PaymentOperationException : Exception
{
    public string? Issue { get; }

    public PaymentOperationException(string message, string? issue = null) : base(message)
    {
        Issue = issue;
    }
}
