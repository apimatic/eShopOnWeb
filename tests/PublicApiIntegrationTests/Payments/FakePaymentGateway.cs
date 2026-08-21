using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace PublicApiIntegrationTests.Payments;

/// <summary>
/// Deterministic in-memory <see cref="IPaymentGateway"/> for endpoint tests — no network, no PayPal.
/// Models just enough behaviour to exercise the full flow (authorize → capture with fee/net →
/// refund → void → vault) and to feed reconciliation.
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public const string DeclineCardNumber = "4000000000000002";

    private int _counter;
    private readonly ConcurrentDictionary<string, (string MerchantReference, decimal Amount, string Currency)> _authorizations = new();
    private readonly List<ReconciliationTransaction> _transactions = new();
    public ConcurrentBag<string> DeletedVaultIds { get; } = new();

    private int Next() => Interlocked.Increment(ref _counter);

    public Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Card is { Number: DeclineCardNumber })
            throw new PaymentGatewayException("PayPal rejected the request: INSTRUMENT_DECLINED.", 422, "debug-decline");

        var n = Next();
        var authId = $"AUTH-{n}";
        _authorizations[authId] = (request.MerchantReference, request.Amount, request.CurrencyCode);
        return Task.FromResult(new AuthorizationResult($"PPO-{n}", authId, "CREATED", DateTimeOffset.Now.AddDays(3)));
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var info = _authorizations.TryGetValue(authorizationId, out var stored)
            ? stored
            : (MerchantReference: "UNKNOWN", Amount: 0m, Currency: "USD");

        var gross = info.Amount;
        var fee = Math.Round(gross * 0.03m, 2);
        var net = gross - fee;
        var captureId = $"CAP-{Next()}";

        _transactions.Add(new ReconciliationTransaction(
            captureId, "S", gross, info.Currency, info.MerchantReference,
            DateTimeOffset.Now, DateTimeOffset.Now));

        return Task.FromResult(new CaptureResult(captureId, "COMPLETED", gross, fee, net, info.Currency));
    }

    public Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var n = Next();
        var authId = $"AUTH-{n}";
        _authorizations[authId] = (_authorizations.TryGetValue(authorizationId, out var s) ? s.MerchantReference : "UNKNOWN", amount, currencyCode);
        return Task.FromResult(new AuthorizationResult(string.Empty, authId, "CREATED", DateTimeOffset.Now.AddDays(3)));
    }

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        _authorizations.TryRemove(authorizationId, out _);
        return Task.CompletedTask;
    }

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RefundResult($"REF-{Next()}", "COMPLETED", amount ?? 0m, currencyCode));

    public Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? payPalCustomerId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var n = Next();
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return Task.FromResult(new VaultedCardResult(
            $"VAULT-{n}", payPalCustomerId ?? $"CUST-{n}", "VISA", last4, card.Expiry));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        DeletedVaultIds.Add(vaultId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReconciliationTransaction> result = _transactions
            .Where(t => t.InitiationDate is { } d && d >= from && d <= to)
            .ToList();
        return Task.FromResult(result);
    }
}
