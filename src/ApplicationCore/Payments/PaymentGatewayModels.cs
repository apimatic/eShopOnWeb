using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string ExpiryMonth,
    string ExpiryYear,
    string SecurityCode,
    string CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Card billing address, as PayPal expects it.</summary>
public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2, // city
    string? AdminArea1, // state / province
    string PostalCode,
    string CountryCode); // ISO 3166-1 alpha-2

/// <summary>
/// A request to authorize (hold) an order total. The money source is either raw <see cref="Card"/>
/// details for a one-off payment, or a previously saved <see cref="VaultTokenId"/>.
/// </summary>
public class AuthorizationRequest
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;

    /// <summary>Correlation reference written onto the PayPal order (custom/invoice id), e.g. the eShop order id.</summary>
    public string OrderReference { get; init; } = default!;

    /// <summary>Idempotency key so a double-click authorizes only once.</summary>
    public string IdempotencyKey { get; init; } = default!;

    public CardDetails? Card { get; init; }
    public string? VaultTokenId { get; init; }
}

public class AuthorizationResult
{
    public string PayPalOrderId { get; init; } = default!;
    public string AuthorizationId { get; init; } = default!;
    public string Status { get; init; } = default!;

    /// <summary>True if PayPal answered with a challenge that needs a shopper to approve in a browser.</summary>
    public bool RequiresCustomerAction { get; init; }
}

public class CaptureResult
{
    public string CaptureId { get; init; } = default!;
    public string Status { get; init; } = default!;
    public decimal GrossAmount { get; init; }
    public decimal PayPalFee { get; init; }
    public decimal NetAmount { get; init; }
    public string Currency { get; init; } = default!;
}

public class RefundRequest
{
    public string CaptureId { get; init; } = default!;
    public decimal? Amount { get; init; } // null = full remaining refund
    public string Currency { get; init; } = default!;
    public string IdempotencyKey { get; init; } = default!;
}

public class RefundResult
{
    public string RefundId { get; init; } = default!;
    public string Status { get; init; } = default!;
    public decimal Amount { get; init; }
}

public class VaultedCardResult
{
    public string VaultTokenId { get; init; } = default!;
    public string? CardBrand { get; init; }
    public string? Last4 { get; init; }
    public string? ExpiryMonth { get; init; }
    public string? ExpiryYear { get; init; }
}

/// <summary>One row of PayPal's own transaction record, for reconciliation.</summary>
public class GatewayTransaction
{
    public string TransactionId { get; init; } = default!;
    public string? Status { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }

    /// <summary>Any reference PayPal carries that can correlate to an eShop order (invoice/custom id).</summary>
    public string? ReferenceId { get; init; }
    public DateTimeOffset? Date { get; init; }
    public string? EventCode { get; init; }
}
