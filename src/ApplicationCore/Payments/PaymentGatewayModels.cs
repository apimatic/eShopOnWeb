using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Classification of a payment-gateway failure, mapped to HTTP outcomes at the API boundary.</summary>
public enum PaymentErrorType
{
    /// <summary>The request was invalid before reaching the gateway (validation).</summary>
    Validation = 1,

    /// <summary>The requested order/payment method does not exist or belongs to another shopper.</summary>
    NotFound = 2,

    /// <summary>The gateway account is not permitted to perform the operation (e.g. vault token deletion).</summary>
    Forbidden = 3,

    /// <summary>The card was declined or the payment instrument was rejected.</summary>
    Declined = 3,

    /// <summary>The authorization was stale and could not be renewed; operator action is needed.</summary>
    StaleAuthorization = 4,

    /// <summary>The request conflicts with the current state (e.g. refund beyond the capture).</summary>
    Conflict = 5,

    /// <summary>The gateway returned an unexpected error or an unreadable response.</summary>
    ProviderError = 6,

    /// <summary>The gateway could not be reached (network/transport failure or timeout).</summary>
    TransportFailure = 7
}

/// <summary>A structured payment error carried by service and gateway results.</summary>
public class PaymentError
{
    public PaymentErrorType Type { get; }
    public string Message { get; }

    public PaymentError(PaymentErrorType type, string message)
    {
        Type = type;
        Message = message;
    }
}

/// <summary>Outcome of a gateway call: either a value or a classified error.</summary>
public class GatewayResult<T>
{
    public bool Succeeded { get; init; }
    public T? Value { get; init; }
    public PaymentError? Error { get; init; }

    public static GatewayResult<T> Success(T value) => new() { Succeeded = true, Value = value };
    public static GatewayResult<T> Failure(PaymentError error) => new() { Succeeded = false, Error = error };
}

/// <summary>Raw card details for a one-off payment or vaulting. Never persisted by the application.</summary>
public class CardInput
{
    public string Name { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;

    /// <summary>Card expiry month, 1-12.</summary>
    public int ExpiryMonth { get; init; }

    /// <summary>Card expiry year, four digits.</summary>
    public int ExpiryYear { get; init; }

    public string SecurityCode { get; init; } = string.Empty;
    public BillingAddressInput? BillingAddress { get; init; }
}

/// <summary>Billing address for a card payment.</summary>
public class BillingAddressInput
{
    public string CountryCode { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? PostalCode { get; init; }
}

public class AuthorizeOutcome
{
    public string PayPalOrderId { get; init; } = string.Empty;
    public string AuthorizationId { get; init; } = string.Empty;
    public string AuthorizationStatus { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class ReauthorizeOutcome
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string AuthorizationStatus { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class AuthorizationInfo
{
    public string AuthorizationId { get; init; } = string.Empty;
    public string AuthorizationStatus { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
}

public class CaptureOutcome
{
    public string CaptureId { get; init; } = string.Empty;
    public string CaptureStatus { get; init; } = string.Empty;
    public decimal CapturedAmount { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>PayPal's fee on the capture, when reported.</summary>
    public decimal? PayPalFee { get; init; }

    /// <summary>Net proceeds to the merchant, when reported.</summary>
    public decimal? NetAmount { get; init; }
}

public class RefundOutcome
{
    public string RefundId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>Total refunded on the capture so far, as reported by PayPal.</summary>
    public decimal? TotalRefundedAmount { get; init; }
}

public class VaultOutcome
{
    public string TokenId { get; init; } = string.Empty;
    public string? CustomerId { get; init; }
    public string Brand { get; init; } = string.Empty;
    public string LastDigits { get; init; } = string.Empty;
    public string? Expiry { get; init; }
    public string? CardholderName { get; init; }
}

/// <summary>One of PayPal's own transactions, as reported by its transaction search.</summary>
public class ReconciliationTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public string? EventCode { get; init; }
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public decimal? FeeAmount { get; init; }
    public DateTimeOffset? InitiationDate { get; init; }
    public string? InvoiceId { get; init; }
    public string? ReferenceId { get; init; }
}

public class ReconciliationResult
{
    public IReadOnlyList<ReconciliationTransaction> Transactions { get; init; } =
        Array.Empty<ReconciliationTransaction>();

    /// <summary>When PayPal last refreshed its transaction reporting, if reported.</summary>
    public string? LastRefreshedDatetime { get; init; }
}
