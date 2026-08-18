using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// In-memory stand-in for PayPal so the endpoint/flow tests run without network or credentials. It mimics the
/// stateful behaviour the real gateway relies on (an authorization remembers its amount; capture echoes it).
/// </summary>
public class FakePayPalGateway : IPayPalPaymentGateway
{
    private int _seq;
    private readonly ConcurrentDictionary<string, (decimal amount, string currency)> _authorizations = new();
    private readonly ConcurrentDictionary<string, (decimal amount, string currency)> _captures = new();
    private readonly ConcurrentDictionary<string, bool> _liveVaultTokens = new();

    public string Currency => "USD";

    public List<PayPalTransaction> TransactionsToReturn { get; } = new();

    public Task<PayPalAuthorization> AuthorizeAsync(decimal amount, string currency, CardDetails? card,
        string? vaultId, string idempotencyKey, string? customId, CancellationToken cancellationToken)
    {
        if (vaultId is not null && !_liveVaultTokens.ContainsKey(vaultId))
            throw new PaymentGatewayException("authorize: vaulted card no longer exists.", 404);

        var authId = $"AUTH-{Next()}";
        _authorizations[authId] = (amount, currency);
        return Task.FromResult(new PayPalAuthorization($"PPO-{Next()}", authId, "CREATED",
            DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task<PayPalCapture> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (amount, currency) = _authorizations.TryGetValue(authorizationId, out var a) ? a : (0m, Currency);
        var captureId = $"CAP-{Next()}";
        var fee = Math.Round(amount * 0.03m, 2);
        _captures[captureId] = (amount, currency);
        return Task.FromResult(new PayPalCapture(captureId, "COMPLETED", amount, currency, fee, amount - fee));
    }

    public Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken)
    {
        var authId = $"AUTH-{Next()}";
        _authorizations[authId] = (amount, currency);
        return Task.FromResult(new PayPalAuthorization(string.Empty, authId, "CREATED",
            DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        _authorizations.TryRemove(authorizationId, out _);
        return Task.CompletedTask;
    }

    public Task<PayPalRefund> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var captured = _captures.TryGetValue(captureId, out var c) ? c.amount : 0m;
        var refundAmount = amount ?? captured;
        return Task.FromResult(new PayPalRefund($"REF-{Next()}", "COMPLETED", refundAmount, currency));
    }

    public Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        var last4 = digits.Length >= 4 ? digits[^4..] : digits;
        var vaultId = $"VAULT-{Next()}";
        _liveVaultTokens[vaultId] = true;
        return Task.FromResult(new PayPalVaultedCard(vaultId, "VISA", last4, card.Expiry));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        _liveVaultTokens.TryRemove(vaultId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<PayPalTransaction>>(TransactionsToReturn.ToList());
    }

    private int Next() => Interlocked.Increment(ref _seq);
}
