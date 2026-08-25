using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalOptions _payPalOptions;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPalGateway,
        Microsoft.Extensions.Options.IOptions<PayPalOptions> payPalOptions,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalGateway = payPalGateway;
        _payPalOptions = payPalOptions.Value;
        _logger = logger;
    }

    public async Task<Payment> AuthorizeOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if ((card is null) == (savedPaymentMethodId is null))
        {
            throw new ArgumentException("Exactly one of a card or a saved payment method id must be supplied.");
        }

        var order = await GetOwnedOrderAsync(orderId, buyerId, ct);

        var existingPayment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (existingPayment is not null && existingPayment.Status != PaymentStatus.Pending)
        {
            // Already authorized (or beyond) - idempotent no-op, no second hold placed.
            return existingPayment;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {orderId} is not awaiting payment (current status: {order.Status}).");
        }

        string? vaultId = null;
        if (savedPaymentMethodId is not null)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
            if (method is null || method.BuyerId != buyerId)
            {
                throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);
            }
            vaultId = method.PayPalVaultId;
        }

        var amount = order.Total();
        var payPalRequestId = $"authorize-order-{orderId}";

        var authorization = await _payPalGateway.AuthorizeAsync(amount, _payPalOptions.Currency, payPalRequestId, card, vaultId, ct);

        var payment = existingPayment ?? new Payment(orderId, _payPalOptions.Currency, amount);
        payment.RecordAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt, payPalRequestId);

        if (existingPayment is null)
        {
            await _paymentRepository.AddAsync(payment, ct);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {orderId} has no active authorization to capture (payment status: {payment.Status}).");
        }

        var authorizationId = payment.PayPalAuthorizationId!;

        var isStale = payment.AuthorizationExpiresAt is not null && payment.AuthorizationExpiresAt <= DateTimeOffset.UtcNow;
        if (isStale)
        {
            authorizationId = await RenewAuthorizationAsync(orderId, payment, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPalGateway.CaptureAsync(authorizationId, payment.Amount, payment.Currency, $"capture-order-{orderId}", ct);
        }
        catch (PayPalGatewayException) when (!isStale)
        {
            // The authorization looked fresh but PayPal rejected the capture anyway (e.g. it was
            // voided upstream or expired sooner than our locally cached ExpiresAt suggested).
            // Renew once and retry, per the requirement that a stale hold be renewed rather than
            // failing fulfilment outright.
            _logger.LogWarning($"Capture failed for order {orderId} on a non-expired authorization; attempting one reauthorize-and-retry.");
            authorizationId = await RenewAuthorizationAsync(orderId, payment, ct);
            capture = await _payPalGateway.CaptureAsync(authorizationId, payment.Amount, payment.Currency, $"capture-order-{orderId}-retry", ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.FeeAmount, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        return payment;
    }

    private async Task<string> RenewAuthorizationAsync(int orderId, Payment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPalGateway.ReauthorizeAsync(payment.PayPalAuthorizationId!, payment.Amount, payment.Currency, $"reauth-order-{orderId}", ct);
            payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return reauth.AuthorizationId;
        }
        catch (PayPalGatewayException ex)
        {
            throw new PayPalGatewayException(
                $"Order {orderId}'s payment authorization has expired and PayPal could not renew it. " +
                $"An operator must resolve this manually (e.g. ask the shopper to pay again). {ex.Message}",
                isProviderRejection: true,
                debugId: ex.DebugId,
                issues: ex.Issues,
                innerException: ex);
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            if (payment is null || payment.Status != PaymentStatus.Authorized)
            {
                throw new InvalidOrderStateException($"Order {orderId} is authorized but has no active hold to release.");
            }

            await _payPalGateway.VoidAsync(payment.PayPalAuthorizationId!, $"void-order-{orderId}", ct);
            payment.RecordVoid("VOIDED");
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Refund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (amount is not null)
        {
            Guard.Against.NegativeOrZero(amount.Value, nameof(amount));
        }

        var order = await GetOwnedOrderAsync(orderId, buyerId, ct);
        var payment = await GetPaymentAsync(orderId, ct);

        var alreadyProcessed = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (alreadyProcessed is not null)
        {
            return alreadyProcessed;
        }

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {orderId} has not been fulfilled, so it cannot be refunded (payment status: {payment.Status}).");
        }

        var refundResult = await _payPalGateway.RefundAsync(payment.PayPalCaptureId!, amount, payment.Currency, idempotencyKey, ct);

        var refund = payment.RecordRefund(refundResult.RefundId, refundResult.Status, refundResult.Amount, idempotencyKey, refundResult.TotalRefundedAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkRefunded(isFullRefund: payment.Status == PaymentStatus.Refunded);
        await _orderRepository.UpdateAsync(order, ct);

        return refund;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            // Hide existence of another buyer's order rather than revealing it via 403.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new InvalidOrderStateException($"Order {orderId} has no payment on file.");
        }
        return payment;
    }
}
