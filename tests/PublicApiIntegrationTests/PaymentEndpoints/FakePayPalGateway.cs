using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// A deterministic in-memory stand-in for the PayPal gateway, so endpoint/orchestration behaviour (state
/// machine, idempotency gating, ownership, reconciliation matching) can be tested without live PayPal.
/// Call counters let tests assert that a double-click does not authorize/capture/refund twice.
/// </summary>
public class FakePayPalGateway : IPayPalPaymentGateway
{
    private readonly ConcurrentDictionary<string, AuthRecord> _auths = new();
    private readonly ConcurrentDictionary<string, CaptureRecord> _captures = new();
    private readonly ConcurrentDictionary<string, PayPalVaultResult> _vault = new();

    public int AuthorizeCalls;
    public int CaptureCalls;
    public int RefundCalls;
    public int VoidCalls;

    private sealed record AuthRecord(string OrderId, string AuthId, decimal Amount, string InvoiceReference);
    private sealed record CaptureRecord(string CaptureId, decimal Gross, decimal Fee, decimal Net, string InvoiceReference, DateTimeOffset At);

    public Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken ct)
    {
        Interlocked.Increment(ref AuthorizeCalls);
        var orderId = "ORDER-" + Guid.NewGuid().ToString("N")[..12];
        var authId = "AUTH-" + Guid.NewGuid().ToString("N")[..12];
        _auths[authId] = new AuthRecord(orderId, authId, command.Amount, command.InvoiceReference);
        return Task.FromResult(new PayPalAuthorizationResult
        {
            PayPalOrderId = orderId,
            AuthorizationId = authId,
            Status = "CREATED",
            AuthorizedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
        });
    }

    public Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
        Task.FromResult(new PayPalAuthorizationState
        {
            AuthorizationId = authorizationId,
            Status = "CREATED",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
        });

    public Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var newAuthId = "AUTH-" + Guid.NewGuid().ToString("N")[..12];
        if (_auths.TryGetValue(authorizationId, out var prev))
            _auths[newAuthId] = prev with { AuthId = newAuthId };
        return Task.FromResult(new PayPalAuthorizationState
        {
            AuthorizationId = newAuthId,
            Status = "CREATED",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
        });
    }

    public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        Interlocked.Increment(ref CaptureCalls);
        _auths.TryGetValue(authorizationId, out var auth);
        var amount = auth?.Amount ?? 0m;
        var invoiceRef = auth?.InvoiceReference ?? string.Empty;
        var fee = Math.Round(amount * 0.0349m + 0.49m, 2);
        var net = amount - fee;
        var captureId = "CAP-" + Guid.NewGuid().ToString("N")[..12];
        _captures[captureId] = new CaptureRecord(captureId, amount, fee, net, invoiceRef, DateTimeOffset.UtcNow);
        return Task.FromResult(new PayPalCaptureResult
        {
            CaptureId = captureId,
            Status = "COMPLETED",
            CapturedAt = DateTimeOffset.UtcNow,
            GrossAmount = amount,
            PayPalFee = fee,
            NetAmount = net,
        });
    }

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        Interlocked.Increment(ref VoidCalls);
        _auths.TryRemove(authorizationId, out _);
        return Task.CompletedTask;
    }

    public Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct)
    {
        Interlocked.Increment(ref RefundCalls);
        _captures.TryGetValue(captureId, out var cap);
        var refundAmount = amount ?? cap?.Gross ?? 0m;
        return Task.FromResult(new PayPalRefundResult
        {
            RefundId = "REF-" + Guid.NewGuid().ToString("N")[..12],
            Status = "COMPLETED",
            Amount = refundAmount,
        });
    }

    public Task<PayPalOrderState?> GetOrderAsync(string payPalOrderId, CancellationToken ct) =>
        Task.FromResult<PayPalOrderState?>(new PayPalOrderState { Status = "COMPLETED" });

    public Task<PayPalVaultResult> VaultCardAsync(PayPalVaultCardCommand command, CancellationToken ct)
    {
        var vaultId = "VAULT-" + Guid.NewGuid().ToString("N")[..12];
        var last4 = new string(command.Card.Number.Where(char.IsDigit).ToArray());
        last4 = last4.Length >= 4 ? last4[^4..] : last4;
        var result = new PayPalVaultResult
        {
            VaultId = vaultId,
            PayPalCustomerId = command.PayPalCustomerId ?? "CUST-" + Guid.NewGuid().ToString("N")[..8],
            CardBrand = "VISA",
            LastFourDigits = last4,
            Expiry = command.Card.Expiry,
        };
        _vault[vaultId] = result;
        return Task.FromResult(result);
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        _vault.TryRemove(vaultId, out _);
        return Task.CompletedTask;
    }

    public Task<PayPalReconciliationResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var txns = _captures.Values
            .Where(c => c.At >= from && c.At <= to)
            .Select(c => new PayPalTransaction
            {
                TransactionId = c.CaptureId,
                Status = "S",
                Amount = c.Gross,
                CurrencyCode = "USD",
                InvoiceId = c.InvoiceReference,
                CustomField = c.InvoiceReference,
                InitiationDate = c.At,
            })
            .ToList();

        return Task.FromResult(new PayPalReconciliationResult
        {
            Transactions = txns,
            WindowsScanned = 1,
            PagesScanned = 1,
            Complete = true,
        });
    }
}
