using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// In-memory stand-ins for the PayPal gateways so the endpoint/persistence wiring can be tested
/// without calling PayPal. They mimic the real contract's shapes (ids, statuses, fee breakdown).
/// </summary>
public class FakePaymentGateway : IPayPalPaymentGateway
{
    private int _counter;

    public Task<GatewayAuthorization> AuthorizeOrderAsync(AuthorizeGatewayRequest request, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _counter);
        return Task.FromResult(new GatewayAuthorization(
            PayPalOrderId: $"PPO-{n}",
            OrderStatus: "COMPLETED",
            AuthorizationId: $"AUTH-{n}",
            AuthorizationStatus: "CREATED",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(29),
            RequiresPayerAction: false));
    }

    public Task<GatewayCapture> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var fee = Math.Round(amount * 0.03m + 0.30m, 2, MidpointRounding.AwayFromZero);
        return Task.FromResult(new GatewayCapture($"CAP-{authorizationId}", "COMPLETED", amount, fee, amount - fee, currency));
    }

    public Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayAuthorization(string.Empty, "UNKNOWN", $"REAUTH-{authorizationId}", "CREATED", DateTimeOffset.UtcNow.AddDays(29), false));

    public Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<GatewayRefund> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _counter);
        return Task.FromResult(new GatewayRefund($"REF-{n}", "COMPLETED", amount ?? 0m, currency));
    }

    public Task<GatewayAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayAuthorization(string.Empty, "UNKNOWN", authorizationId, "CREATED", DateTimeOffset.UtcNow.AddDays(29), false));
}

public class FakeVaultGateway : IPayPalVaultGateway
{
    private int _counter;

    public Task<VaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _counter);
        var last4 = request.Card.Number.Length >= 4 ? request.Card.Number[^4..] : "0000";
        return Task.FromResult(new VaultedCard($"VAULT-{n}", "CUST-1", "VISA", last4, request.Card.Expiry, "CREDIT", request.Card.CardholderName));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public class FakeReportingGateway : IPayPalReportingGateway
{
    public Task<IReadOnlyList<ReportedTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ReportedTransaction>>(new List<ReportedTransaction>());
}
