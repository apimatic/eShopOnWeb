using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace PublicApiIntegrationTests.Fakes;

// A deterministic, no-network stand-in for the real PayPal gateway. Registered in place of
// PayPalPaymentGateway for HTTP-level integration tests, so the endpoint/auth/ownership/state-machine
// wiring can be exercised hermetically - the real sandbox integration is verified separately.
public class FakePaymentGateway : IPaymentGateway
{
    // A card number that simulates a decline, so PaymentDeclinedException mapping can be tested.
    public const string DeclinedCardNumber = "4000000000000002";

    private int _counter;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _authorizedAmounts = new();

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        if (card.Number == DeclinedCardNumber)
        {
            throw new PaymentDeclinedException("The card was declined.");
        }

        return Task.FromResult(NewAuthorization(amount));
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken ct = default)
        => Task.FromResult(NewAuthorization(amount));

    public Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, CancellationToken ct = default)
    {
        var renewed = $"auth-{Next()}";
        if (_authorizedAmounts.TryGetValue(authorizationId, out var amount))
        {
            _authorizedAmounts[renewed] = amount;
        }

        return Task.FromResult(new ReauthorizationResult(renewed, "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var amount = _authorizedAmounts.TryGetValue(authorizationId, out var a) ? a : 0m;
        var fee = Math.Round(amount * 0.03m, 2);
        return Task.FromResult(new CaptureResult($"capture-{Next()}", "COMPLETED", amount, fee, amount - fee, DateTimeOffset.UtcNow));
    }

    public Task VoidAsync(string authorizationId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
        => Task.FromResult(new RefundResult($"refund-{Next()}", "COMPLETED", amount));

    public Task<SavedCardResult> SaveCardAsync(CardDetails card, string merchantCustomerId, CancellationToken ct = default)
        => Task.FromResult(new SavedCardResult($"vault-{Next()}", "VISA", card.Number.Length >= 4 ? card.Number[^4..] : card.Number, card.ExpiryMonth, card.ExpiryYear));

    public Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TransactionRecord>>(Array.Empty<TransactionRecord>());

    private AuthorizationResult NewAuthorization(decimal amount)
    {
        var authorizationId = $"auth-{Next()}";
        _authorizedAmounts[authorizationId] = amount;
        return new AuthorizationResult($"paypal-order-{Next()}", authorizationId, "CREATED", DateTimeOffset.UtcNow.AddDays(29));
    }

    private int Next() => Interlocked.Increment(ref _counter);
}
