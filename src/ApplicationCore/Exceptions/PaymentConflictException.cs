using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment operation conflicts with the current state of the
/// order or payment (e.g. capturing an authorization that can no longer be
/// renewed). The message is intended to be actionable by an operator.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) {}
}
