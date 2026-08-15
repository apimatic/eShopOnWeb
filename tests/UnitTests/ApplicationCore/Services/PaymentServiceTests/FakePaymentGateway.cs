using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

/// <summary>
/// A hand-rolled fake of <see cref="IPaymentGateway"/> that records calls and lets a test script the
/// processor's responses — so the service's orchestration is tested without touching PayPal.
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    public int AuthorizeCardCalls { get; private set; }
    public int AuthorizeVaultCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int ReauthorizeCalls { get; private set; }
    public int VoidCalls { get; private set; }
    public readonly List<(string captureId, decimal? amount, string key)> Refunds = new();

    public bool FailFirstCapture { get; set; }
    public bool ReauthorizeShouldFail { get; set; }
    public DateTimeOffset? NextAuthorizationExpiry { get; set; } = DateTimeOffset.UtcNow.AddDays(3);
    public List<GatewayTransaction> Transactions { get; } = new();

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        AuthorizeCardCalls++;
        return Task.FromResult(new AuthorizationResult("PP-ORDER", "AUTH-1", "CREATED", NextAuthorizationExpiry));
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        AuthorizeVaultCalls++;
        return Task.FromResult(new AuthorizationResult("PP-ORDER", "AUTH-VAULT", "CREATED", NextAuthorizationExpiry));
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        CaptureCalls++;
        if (FailFirstCapture && CaptureCalls == 1)
        {
            throw new PaymentException("AUTHORIZATION_EXPIRED");
        }
        return Task.FromResult(new CaptureResult("CAP-1", "COMPLETED", 100m, 3m, 97m));
    }

    public Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        ReauthorizeCalls++;
        if (ReauthorizeShouldFail)
        {
            throw new PaymentException("AUTHORIZATION_CANNOT_BE_REAUTHORIZED");
        }
        return Task.FromResult(new AuthorizationResult("PP-ORDER", "AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        VoidCalls++;
        return Task.CompletedTask;
    }

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Refunds.Add((captureId, amount, idempotencyKey));
        return Task.FromResult(new RefundResult($"REFUND-{Refunds.Count}", "COMPLETED"));
    }

    public Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default) =>
        Task.FromResult(new VaultedCard("VAULT-1", "VISA", "1111", card.ExpiryMonth, card.ExpiryYear, card.CardholderName));

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult((IReadOnlyList<GatewayTransaction>)Transactions);
}
