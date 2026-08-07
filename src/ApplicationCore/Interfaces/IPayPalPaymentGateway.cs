using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the PayPal payment provider. Everything the application needs to charge a card,
/// refund a payment and vault (save) a card for reuse is expressed here in provider-neutral domain
/// terms — no PayPal SDK types leak past this seam. The concrete implementation lives in Infrastructure.
/// <para>
/// All operations that move money or create a vault entry accept an <c>idempotencyKey</c>: the same
/// key with the same request is guaranteed by the provider to take effect at most once, so a retry
/// (or a double-click) never produces a duplicate charge, refund or saved card.
/// </para>
/// Implementations throw <see cref="Exceptions.PaymentGatewayException"/> when the provider rejects or
/// fails an operation.
/// </summary>
public interface IPayPalPaymentGateway
{
    /// <summary>Charge a raw card for a one-off payment and return the resulting authorization (order + capture ids).</summary>
    Task<PaymentAuthorization> ChargeCardAsync(Money amount, CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Charge a previously vaulted card (by its vault token) for a payment.</summary>
    Task<PaymentAuthorization> ChargeVaultedCardAsync(Money amount, string vaultId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Refund a captured payment in full, by its capture id.</summary>
    Task<RefundReceipt> RefundAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Vault (save) a card for later reuse and return its token + safe display metadata. Full card details are not returned.</summary>
    Task<VaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Delete a vaulted card by its token so it can no longer be used.</summary>
    Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default);
}

/// <summary>An amount of money in a given ISO-4217 currency (USD for this integration).</summary>
public record Money(decimal Amount, string CurrencyCode)
{
    public static Money Usd(decimal amount) => new(amount, "USD");
}

/// <summary>Raw card details for a one-off charge or to vault. Handled in-flight only; never persisted or logged.</summary>
public record CardDetails(
    string CardholderName,
    string Number,
    int ExpiryMonth,
    int ExpiryYear,
    string SecurityCode,
    BillingAddress? BillingAddress);

/// <summary>A card billing address. <see cref="CountryCode"/> is a required ISO-3166-1 alpha-2 code.</summary>
public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

/// <summary>Safe, non-sensitive card display metadata returned by the provider.</summary>
public record CardDisplay(string? Brand, string Last4, int? ExpiryMonth, int? ExpiryYear);

/// <summary>The result of a successful charge: the PayPal order id and the capture id used for refunds, plus safe card display.</summary>
public record PaymentAuthorization(string PayPalOrderId, string CaptureId, CardDisplay Card);

/// <summary>The result of a successful full refund.</summary>
public record RefundReceipt(string RefundId);

/// <summary>The result of vaulting a card: the reusable vault token and safe card display metadata.</summary>
public record VaultedCard(string VaultId, CardDisplay Card);
