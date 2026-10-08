using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>A scripted payment provider that records every call it receives.</summary>
public class FakePaymentGateway : IPaymentGateway
{
    public string Currency { get; set; } = "USD";

    public Func<CardPaymentRequest, CancellationToken, Task<CardPaymentResult>> OnCharge { get; set; } =
        (request, _) => Task.FromResult(Authorised(request));

    public Func<ProviderRefundRequest, CancellationToken, Task<ProviderRefundResult>> OnRefund { get; set; } =
        (request, _) => Task.FromResult(new ProviderRefundResult($"PSP-REFUND-{request.IdempotencyKey[..8]}", "received"));

    public ConcurrentQueue<CardPaymentRequest> Charges { get; } = new();
    public ConcurrentQueue<ProviderRefundRequest> Refunds { get; } = new();

    public Task<CardPaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken deadline)
    {
        Charges.Enqueue(request);
        return OnCharge(request, deadline);
    }

    public Task<ProviderRefundResult> RefundAsync(ProviderRefundRequest request, CancellationToken deadline)
    {
        Refunds.Enqueue(request);
        return OnRefund(request, deadline);
    }

    public static CardPaymentResult Authorised(CardPaymentRequest request) =>
        new(CardPaymentOutcome.Authorised, "Authorised", $"PSP-{request.IdempotencyKey[..8]}", null, null, request.Currency, request.AmountMinor);

    public static CardPaymentResult Refused(string reason) =>
        new(CardPaymentOutcome.Refused, "Refused", "PSP-REFUSED", reason, "6", null, null);
}
