using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money movement for an order across the eShop aggregates and the PayPal gateway.
/// Each action is separately invocable and idempotent in effect: a double-click never authorizes or
/// captures twice (the persisted Order/Payment state is the durable claim, and a deterministic
/// PayPal request id makes a resend dedupe provider-side).
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    // A small buffer before an authorization's honor period lapses, so we renew proactively.
    private static readonly TimeSpan StaleBuffer = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<OrderView> PayAsync(int orderId, string buyerId, PayInstruction instruction,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new BuyerOrderByIdSpec(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: a repeated pay for an already-authorized order returns the existing hold.
        if (order.PaymentStatus == OrderPaymentStatus.Authorized && payment?.IsAuthorized == true)
            return PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode);
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw new PaymentException($"Order {orderId} is {order.PaymentStatus} and can no longer be paid.",
                HttpStatusCode.Conflict);

        // Resolve the funding source: a saved card (by id, scoped to the caller) or one-off card.
        string? vaultId = null;
        CardDetails? card = null;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpec(savedId, buyerId), cancellationToken)
                ?? throw new PaymentException($"Saved payment method {savedId} was not found.", HttpStatusCode.NotFound);
            vaultId = saved.VaultId;
        }
        else if (instruction.Card is not null)
        {
            card = instruction.Card;
        }
        else
        {
            throw new ArgumentException("Provide either card details or a saved payment method id.");
        }

        var amount = order.Total();
        var auth = await _gateway.AuthorizeAsync(
            new PaymentAuthorizationRequest(order.PaymentReference, amount, card, vaultId, $"eShopOnWeb order {orderId}"),
            cancellationToken);

        var kind = vaultId is not null ? "saved_card" : "card";
        if (payment is null)
        {
            payment = new Payment(orderId, buyerId, _gateway.CurrencyCode, amount, kind, auth.PaymentMethodDescription);
            payment.RecordAuthorization(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            payment.RecordAuthorization(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: paypalOrder={auth.PayPalOrderId} auth={auth.AuthorizationId} status={auth.Status}");
        return PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode);
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Paid)
            return PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode); // idempotent
        if (payment is null || !payment.IsAuthorized || order.PaymentStatus != OrderPaymentStatus.Authorized)
            throw new PaymentException($"Order {orderId} is {order.PaymentStatus} and cannot be fulfilled.",
                HttpStatusCode.Conflict);

        var idBase = order.PaymentReference;

        // Renew a hold that has gone (or is about to go) stale, before capturing.
        if (payment.AuthorizationExpiresAt is DateTimeOffset exp && exp - StaleBuffer <= DateTimeOffset.UtcNow)
        {
            await ReauthorizeAsync(payment, idBase, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, $"capture-{idBase}", amount: null, cancellationToken);
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            // Stale after all — renew and capture once more; if it cannot be renewed, ReauthorizeAsync
            // surfaces an operator-actionable message.
            await ReauthorizeAsync(payment, idBase, cancellationToken);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, $"capture-{idBase}", amount: null, cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkPaid();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Fulfilled order {orderId}: capture={capture.CaptureId} status={capture.Status} captured={capture.CapturedAmount} fee={capture.PayPalFee} net={capture.NetAmount}");
        return PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode);
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            return PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode); // idempotent
        if (order.PaymentStatus is OrderPaymentStatus.Paid or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            throw new PaymentException($"Order {orderId} has already been captured; refund it instead of cancelling.",
                HttpStatusCode.Conflict);

        // Release the hold if one exists (an order still awaiting payment has nothing to void).
        if (payment is { IsAuthorized: true, IsCaptured: false })
        {
            var result = await _gateway.VoidAsync(payment.AuthorizationId!, $"void-{order.PaymentReference}", cancellationToken);
            payment.RecordVoid(result.Status);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}; held funds released.");
        return PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode);
    }

    public async Task<(string RefundId, OrderView Order)> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required for a refund.", nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new BuyerOrderByIdSpec(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (payment is null || !payment.IsCaptured)
            throw new PaymentException($"Order {orderId} has not been captured; there is nothing to refund.",
                HttpStatusCode.Conflict);

        // Idempotency: the same key must never refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return (existing.RefundId, PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode));

        var remaining = payment.RemainingRefundable;
        if (remaining <= 0m)
            throw new PaymentException($"Order {orderId} has been fully refunded.", HttpStatusCode.Conflict);

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new ArgumentException("Refund amount must be positive.", nameof(amount));
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
            throw new PaymentException(
                $"Refund of {refundAmount} exceeds the refundable balance of {remaining}.", HttpStatusCode.Conflict);

        var refund = await _gateway.RefundAsync(payment.CaptureId!, $"refund-{idempotencyKey}", refundAmount, cancellationToken);

        payment.RecordRefund(refund.RefundId, refundAmount, refund.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkRefunded(fullyRefunded: payment.RemainingRefundable <= 0m);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Refunded order {orderId}: refund={refund.RefundId} amount={refundAmount} status={refund.Status}");
        return (refund.RefundId, PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode));
    }

    public async Task<OrderView?> GetOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new BuyerOrderByIdSpec(orderId, buyerId), cancellationToken);
        if (order is null) return null;
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        return PaymentViewMapper.ToView(order, payment, _gateway.CurrencyCode);
    }

    private async Task ReauthorizeAsync(Payment payment, Guid idBase, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, $"reauth-{idBase}",
                payment.AuthorizedAmount, cancellationToken);
            payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Reauthorized order {payment.OrderId}: auth={reauth.AuthorizationId} status={reauth.Status}");
        }
        catch (PaymentException ex)
        {
            // A hold past its reauthorization window cannot be renewed — tell the operator plainly.
            throw new PaymentException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed. " +
                "Ask the shopper to place and pay for the order again.",
                HttpStatusCode.Conflict, ex.ProviderCode, ex.DebugId, ex);
        }
    }

    private static bool IsExpiredAuthorization(PaymentException ex) =>
        ex.ProviderCode is "AUTHORIZATION_EXPIRED" or "AUTH_CAPTURE_CURRENCY_MISMATCH" ||
        (ex.StatusCode == HttpStatusCode.UnprocessableEntity &&
         ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase));
}
