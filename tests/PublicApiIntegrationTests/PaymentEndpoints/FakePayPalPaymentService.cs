using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// A deterministic, network-free stand-in for PayPal so the functional tests exercise the endpoints,
/// auth, ownership and idempotency without touching the sandbox. It remembers authorized amounts so
/// capture/refund amounts stay coherent.
/// </summary>
public class FakePayPalPaymentService : IPayPalPaymentService
{
    private readonly ConcurrentDictionary<string, decimal> _authAmounts = new();

    public string Currency => "USD";

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency,
        CardPaymentDetails card, string idempotencyKey, CancellationToken ct = default)
        => Authorize(amount);

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency,
        string vaultId, string? payPalCustomerId, string idempotencyKey, CancellationToken ct = default)
        => Authorize(amount);

    private Task<AuthorizationResult> Authorize(decimal amount)
    {
        var authId = "AUTH-" + Guid.NewGuid().ToString("N");
        _authAmounts[authId] = amount;
        return Task.FromResult(new AuthorizationResult(
            "PPO-" + Guid.NewGuid().ToString("N"), authId, "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var gross = _authAmounts.TryGetValue(authorizationId, out var a) ? a : 0m;
        var fee = Math.Round(gross * 0.02m, 2);
        return Task.FromResult(new CaptureResult(
            "CAP-" + Guid.NewGuid().ToString("N"), "COMPLETED", gross, fee, gross - fee));
    }

    public Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct = default)
    {
        var authId = "AUTH-" + Guid.NewGuid().ToString("N");
        _authAmounts[authId] = amount;
        return Task.FromResult(new ReauthorizationResult(authId, "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task VoidAsync(string authorizationId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
        => Task.FromResult(new RefundResult(
            "REF-" + Guid.NewGuid().ToString("N"), "COMPLETED", amount, amount));

    public Task<VaultedCardResult> VaultCardAsync(CardPaymentDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct = default)
    {
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return Task.FromResult(new VaultedCardResult(
            "VAULT-" + Guid.NewGuid().ToString("N"), "CUST-" + Guid.NewGuid().ToString("N"),
            "VISA", last4, card.ExpiryYearMonth, card.CardholderName));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(string startDate, string endDate,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PayPalTransactionRecord>>(new List<PayPalTransactionRecord>());
}
