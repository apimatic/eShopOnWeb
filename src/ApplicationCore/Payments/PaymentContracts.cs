namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These values are passed straight to
/// PayPal and are never persisted in this application's database nor written to logs.
/// Mirrors the fields of the PayPal spec's <c>card_request</c> schema.
/// </summary>
public sealed record CardDetails
{
    /// <summary>Primary account number (13–19 digits).</summary>
    public required string Number { get; init; }

    /// <summary>Expiry in ISO-8601 <c>YYYY-MM</c> form (PayPal <c>date_year_month</c>).</summary>
    public required string ExpiryMonthYear { get; init; }

    /// <summary>Card security code / CVV (3–4 digits).</summary>
    public string? SecurityCode { get; init; }

    /// <summary>Cardholder name as it appears on the card.</summary>
    public string? CardholderName { get; init; }

    /// <summary>Billing address (PayPal requires at least the country code when supplied).</summary>
    public CardBillingAddress? BillingAddress { get; init; }
}

/// <summary>
/// Billing address for a card. Field names map to the PayPal spec's card billing address object.
/// </summary>
public sealed record CardBillingAddress
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }

    /// <summary>City / town (PayPal <c>admin_area_2</c>).</summary>
    public string? AdminArea2 { get; init; }

    /// <summary>State / province (PayPal <c>admin_area_1</c>).</summary>
    public string? AdminArea1 { get; init; }

    public string? PostalCode { get; init; }

    /// <summary>Two-letter ISO-3166-1 country code (required by PayPal when a billing address is present).</summary>
    public required string CountryCode { get; init; }
}

/// <summary>Request to charge a raw card for an order amount.</summary>
public sealed record CardChargeRequest
{
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required CardDetails Card { get; init; }

    /// <summary>Idempotency key forwarded as PayPal's <c>PayPal-Request-Id</c>.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Merchant reference recorded on the PayPal purchase unit (e.g. the local order id).</summary>
    public string? CustomId { get; init; }
    public string? Description { get; init; }
}

/// <summary>Request to charge a previously vaulted card token for an order amount.</summary>
public sealed record VaultedCardChargeRequest
{
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required string VaultToken { get; init; }
    public required string IdempotencyKey { get; init; }
    public string? CustomId { get; init; }
    public string? Description { get; init; }
}

/// <summary>Request to store a card in PayPal's vault.</summary>
public sealed record VaultCardRequest
{
    public required CardDetails Card { get; init; }
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// A stable, non-sensitive reference to the owning shopper, recorded as PayPal's
    /// <c>customer.merchant_customer_id</c> when it satisfies PayPal's format constraints.
    /// </summary>
    public string? CustomerReference { get; init; }
}

/// <summary>Outcome of a charge (create + capture) attempt.</summary>
public sealed record GatewayPaymentResult
{
    public required bool Success { get; init; }

    /// <summary>PayPal Checkout order id.</summary>
    public string? PayPalOrderId { get; init; }

    /// <summary>PayPal capture id (the refund target), present on success.</summary>
    public string? CaptureId { get; init; }

    /// <summary>The PayPal order status (e.g. COMPLETED) or capture status.</summary>
    public string? Status { get; init; }

    /// <summary>Human-readable failure reason, present when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>PayPal debug id echoed from the failing response, for support/correlation.</summary>
    public string? DebugId { get; init; }
}

/// <summary>Outcome of a refund attempt.</summary>
public sealed record GatewayRefundResult
{
    public required bool Success { get; init; }
    public string? RefundId { get; init; }
    public string? Status { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DebugId { get; init; }
}

/// <summary>Outcome of a vault (save card) attempt, with a safe description of the stored card.</summary>
public sealed record GatewayVaultResult
{
    public required bool Success { get; init; }
    public string? VaultToken { get; init; }
    public string? Last4 { get; init; }
    public string? Brand { get; init; }
    public string? ExpiryMonthYear { get; init; }
    public string? CardholderName { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DebugId { get; init; }
}
