using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Classifies an application-level payment failure so the HTTP boundary can pick a coherent status.</summary>
public enum PaymentError
{
    /// <summary>The order, payment or saved card does not exist (for the caller).</summary>
    NotFound = 0,

    /// <summary>The caller may not act on this resource (belongs to another shopper).</summary>
    Forbidden = 1,

    /// <summary>The operation conflicts with the current payment state (e.g. already fulfilled).</summary>
    Conflict = 2,

    /// <summary>The request itself is invalid (missing/ambiguous funding, bad amount).</summary>
    InvalidRequest = 3,

    /// <summary>PayPal requires a browser approval (3DS challenge) this integration does not perform.</summary>
    ChallengeRequired = 4
}

/// <summary>An expected, caller-facing payment failure (as opposed to <see cref="PayPalGatewayException"/>).</summary>
public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(PaymentError error, string message) : base(message)
    {
        Error = error;
    }

    public PaymentError Error { get; }
}
