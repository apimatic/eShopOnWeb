using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state of an order's payment: the hold (authorization), the capture, and any refunds.
/// It carries enough of PayPal's own identifiers and current status that a later request (fulfil, cancel,
/// refund) can act on it, not only the request that created it. Part of the <see cref="Order"/> aggregate.
/// </summary>
public class Payment : BaseEntity
{
    private readonly List<Refund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        Amount = amount;
        CurrencyCode = currencyCode;
        // Stable idempotency keys generated once and reused across retries so a double-click
        // never authorizes or captures the shopper twice, even under a race on the PayPal side.
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
    }

    /// <summary>The order total that was (or will be) authorized, to the cent.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    // --- Idempotency keys (PayPal-Request-Id) reused across retries of the same logical operation ---
    public string AuthorizeRequestId { get; private set; }
    public string CaptureRequestId { get; private set; }

    // --- The hold (authorization) ---
    /// <summary>PayPal checkout order id backing this payment.</summary>
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>Safe description of the funding instrument, e.g. "VISA ****1111". Never full card data.</summary>
    public string? InstrumentDescription { get; private set; }

    // --- The capture (money actually taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => !string.IsNullOrEmpty(AuthorizationId);
    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    public void SetAuthorization(string payPalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt, string? instrumentDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        if (!string.IsNullOrEmpty(instrumentDescription))
        {
            InstrumentDescription = instrumentDescription;
        }
    }

    /// <summary>Replace a stale authorization id with the renewed one produced by re-authorization.</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
    }

    public void VoidAuthorization()
    {
        AuthorizationStatus = "VOIDED";
    }

    public decimal TotalRefunded() => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>Amount still refundable: captured amount minus what has already been refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Returns an existing refund for this idempotency key, if the caller has used it before.</summary>
    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Registers a new refund, guarding that a partly-refunded payment never becomes refundable
    /// beyond what was captured.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, decimal amount, string currencyCode)
    {
        if (!IsCaptured)
        {
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");
        }

        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} exceeds the remaining refundable amount of {RefundableRemaining():0.00}.");
        }

        var refund = new Refund(idempotencyKey, amount, currencyCode);
        _refunds.Add(refund);
        return refund;
    }
}
