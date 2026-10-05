using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public enum PaymentGatewayFailure
{
    /// <summary>PayPal rejected the request itself (validation, decline, business rule) — the caller can act on it.</summary>
    Rejected,
    /// <summary>PayPal says the referenced resource does not exist.</summary>
    NotFound,
    /// <summary>PayPal refused our own credentials or permissions — an operator problem, not the caller's.</summary>
    MerchantConfiguration,
    /// <summary>PayPal failed or answered with something unreadable.</summary>
    ProviderError,
    /// <summary>PayPal did not answer in time.</summary>
    Timeout,
    /// <summary>PayPal could not be reached, or the connection dropped mid-call.</summary>
    Unreachable
}

/// <summary>A failure talking to the payment processor, already translated out of SDK types.</summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(PaymentGatewayFailure failure, string message, int? providerStatus = null,
        string? providerErrorName = null, string? providerIssue = null, string? debugId = null, Exception? inner = null,
        bool budgetExhausted = false)
        : base(message, inner)
    {
        BudgetExhausted = budgetExhausted;
        Failure = failure;
        ProviderStatus = providerStatus;
        ProviderErrorName = providerErrorName;
        ProviderIssue = providerIssue;
        DebugId = debugId;
    }

    public PaymentGatewayFailure Failure { get; }
    public int? ProviderStatus { get; }
    public string? ProviderErrorName { get; }
    public string? ProviderIssue { get; }

    /// <summary>True when the request's whole PayPal time budget is spent, so no further call can be made in it.</summary>
    public bool BudgetExhausted { get; }

    /// <summary>PayPal's correlation id for support tickets.</summary>
    public string? DebugId { get; }

    /// <summary>
    /// For a write: the request may have reached PayPal and taken effect, so it must be settled by
    /// re-reading PayPal (or re-sending under the same PayPal-Request-Id), never reported as "nothing happened".
    /// </summary>
    public bool OutcomeUnknown => Failure is PaymentGatewayFailure.Timeout or PaymentGatewayFailure.Unreachable
        || (Failure == PaymentGatewayFailure.ProviderError && ProviderStatus is null or >= 500 or (>= 200 and < 300));
}

public enum PaymentErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unprocessable,
    InProgress
}

/// <summary>A payment request eShop itself refuses (bad input, wrong state, not yours, already running).</summary>
public class PaymentRequestException : Exception
{
    public PaymentRequestException(PaymentErrorKind kind, string code, string message) : base(message)
    {
        Kind = kind;
        Code = code;
    }

    public PaymentErrorKind Kind { get; }
    public string Code { get; }

    public static PaymentRequestException NotFound(string what) => new(PaymentErrorKind.NotFound, "not_found", $"{what} was not found.");
}

/// <summary>
/// A PayPal write whose outcome could not be settled within this request. The state is recorded and will be
/// settled by repeating the same request or by the background sweeper.
/// </summary>
public class PaymentOutcomePendingException : Exception
{
    public PaymentOutcomePendingException(string message, PaymentGatewayException cause) : base(message, cause)
    {
        Cause = cause;
    }

    public PaymentGatewayException Cause { get; }
}
