using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace PublicApiIntegrationTests;

public sealed class FakePayPalGateway : IPayPalGateway
{
    private readonly ConcurrentDictionary<string, GatewayAuthorization> _authorizations = new();
    private readonly ConcurrentDictionary<string, GatewayCapture> _captures = new();
    private int _sequence;
    public string Currency => "USD";
    public int RefundCalls { get; private set; }

    public Task<GatewayOrder> CreateOrderAsync(GatewayCreateOrderRequest request,
        CancellationToken cancellationToken) => Task.FromResult(
        new GatewayOrder($"TEST-ORDER-{Next()}", "CREATED"));

    public Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request,
        CancellationToken cancellationToken)
    {
        var value = new GatewayAuthorization(request.PayPalOrderId, "COMPLETED", $"TEST-AUTH-{Next()}",
            "CREATED", null, request.Amount, request.Currency, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29));
        _authorizations[value.AuthorizationId] = value;
        return Task.FromResult(value);
    }

    public Task<GatewayAuthorization> GetAuthorizationAsync(string payPalOrderId, string authorizationId,
        CancellationToken cancellationToken) => Task.FromResult(_authorizations[authorizationId]);

    public Task<GatewayAuthorization> ReauthorizeAsync(string payPalOrderId, string authorizationId,
        decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken)
    {
        var value = _authorizations[authorizationId] with
        {
            AuthorizationId = $"TEST-REAUTH-{Next()}",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(29)
        };
        _authorizations[value.AuthorizationId] = value;
        return Task.FromResult(value);
    }

    public Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var value = new GatewayCapture($"TEST-CAPTURE-{Next()}", "COMPLETED", null, amount, currency,
            1m, amount - 1m, DateTimeOffset.UtcNow);
        _captures[value.CaptureId] = value;
        return Task.FromResult(value);
    }

    public Task<GatewayCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        Task.FromResult(_captures[captureId]);

    public Task<string> VoidAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken) => Task.FromResult("VOIDED");

    public Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        RefundCalls++;
        var value = amount ?? _captures[captureId].Amount;
        return Task.FromResult(new GatewayRefund($"TEST-REFUND-{Next()}", "COMPLETED", null, value, currency));
    }

    public Task<GatewaySavedCard> SaveCardAsync(string buyerId, CardInput card, string operationId,
        CancellationToken cancellationToken) => Task.FromResult(new GatewaySavedCard(
            $"TEST-TOKEN-{Next()}", $"TEST-CUSTOMER-{buyerId}", "VISA", card.Number[^4..], card.Expiry,
            card.Name, "CREDIT"));

    public Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ReconciliationTransaction>>([]);

    private int Next() => Interlocked.Increment(ref _sequence);
}
