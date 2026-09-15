using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// An in-memory stand-in for the PayPal gateway so the API surface (auth, ownership, idempotency, state,
/// amounts) can be tested without any network calls. Records call counts so tests can assert "once".
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    private int _seq;
    private readonly Dictionary<string, decimal> _authorizedAmounts = new();

    public int AuthorizeCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int RefundCalls { get; private set; }
    public int VoidCalls { get; private set; }
    public HashSet<string> DeletedVaults { get; } = new();
    public List<ReconciliationTransaction> Transactions { get; } = new();

    public Task<GatewayAuthorization> AuthorizeOrderAsync(
        decimal amount, string currencyCode, PaymentInstrument instrument, string idempotencyKey, CancellationToken ct = default)
    {
        AuthorizeCalls++;
        var id = $"AUTH-{++_seq}";
        _authorizedAmounts[id] = amount;
        return Task.FromResult(new GatewayAuthorization($"PPO-{_seq}", id, "CREATED", DateTimeOffset.UtcNow.AddDays(29)));
    }

    public Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
        => Task.FromResult(new GatewayAuthorizationState(authorizationId, "CREATED", DateTimeOffset.UtcNow.AddDays(29)));

    public Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        CaptureCalls++;
        var amount = _authorizedAmounts.TryGetValue(authorizationId, out var a) ? a : 10m;
        var fee = Math.Round(amount * 0.02m + 0.30m, 2);
        return Task.FromResult(new GatewayCapture($"CAP-{++_seq}", "COMPLETED", amount, fee, amount - fee, "USD"));
    }

    public Task<GatewayAuthorizationState> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        var id = $"AUTH-{++_seq}";
        _authorizedAmounts[id] = amount;
        return Task.FromResult(new GatewayAuthorizationState(id, "CREATED", DateTimeOffset.UtcNow.AddDays(29)));
    }

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        VoidCalls++;
        return Task.CompletedTask;
    }

    public Task<GatewayRefund> RefundAsync(
        string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        RefundCalls++;
        return Task.FromResult(new GatewayRefund($"REF-{++_seq}", "COMPLETED", amount ?? 0m, currencyCode));
    }

    public Task<GatewayVaultedCard> VaultCardAsync(GatewayCard card, string idempotencyKey, CancellationToken ct = default)
    {
        var last4 = card.Number.Length >= 4 ? card.Number.Substring(card.Number.Length - 4) : card.Number;
        return Task.FromResult(new GatewayVaultedCard($"VAULT-{++_seq}", "VISA", last4, card.Expiry));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        DeletedVaults.Add(vaultId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<ReconciliationTransaction>)Transactions);
}
