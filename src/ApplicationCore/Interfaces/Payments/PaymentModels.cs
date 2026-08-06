namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Raw card details supplied by the caller for a one-off charge or to be vaulted. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string SecurityCode,
    string CardholderName,
    BillingAddress BillingAddress);

/// <summary>Billing address that accompanies raw card details.</summary>
public record BillingAddress(
    string Line1,
    string City,
    string State,
    string PostalCode,
    string CountryCode);

/// <summary>Result of a successful capture: the gateway order id, the capture id (refund target) and status.</summary>
public record PaymentResult(string GatewayOrderId, string CaptureId, string Status);

/// <summary>Result of a full refund: the refund id and status.</summary>
public record RefundResult(string RefundId, string Status);

/// <summary>A card that has been placed in the gateway vault, with a safe descriptor for display.</summary>
public record VaultedCard(
    string VaultId,
    string? CustomerId,
    string Brand,
    string Last4,
    int ExpiryMonth,
    int ExpiryYear,
    string CardholderName);
