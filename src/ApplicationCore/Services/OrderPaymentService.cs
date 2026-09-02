using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGatewayClient _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGatewayClient gateway,
        IOptions<PayPalSettings> settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<OrderPayment> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken cancellationToken = default)
    {
        if (card is null && savedCardId is null)
        {
            throw new ArgumentException("Either card details or a saved payment method id must be supplied.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (order.Status == OrderStatus.PaymentAuthorized && existing is { Status: PaymentStatus.Authorized })
        {
            // Idempotent retry: the hold is already in place.
            return existing;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        var amount = order.Total();
        var currency = _settings.Currency;

        OrderPayment payment;
        if (existing is { Status: PaymentStatus.AuthorizationPending })
        {
            // A previous attempt created the gateway order but did not finish; resume it.
            payment = existing;
        }
        else
        {
            // custom_id is stable per eShop order; invoice_id must be unique per PayPal
            // transaction (the merchant account enforces invoice uniqueness).
            var requestKey = Guid.NewGuid().ToString("N");
            var gatewayOrderId = await _gateway.CreateOrderAsync(amount, currency, $"eshop-order-{orderId}",
                $"eshop-order-{orderId}-{requestKey}", $"eshop-{requestKey}-create", cancellationToken);
            payment = new OrderPayment(orderId, buyerId, amount, currency, gatewayOrderId, requestKey);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        GatewayAuthorization authorization;
        if (savedCardId is not null)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(savedCardId.Value), cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new ArgumentException($"Saved payment method {savedCardId} was not found.");
            }
            authorization = await _gateway.AuthorizeWithVaultedCardAsync(payment.PayPalOrderId, savedCard.PaymentTokenId, $"eshop-{payment.RequestKey}-authorize", cancellationToken);
        }
        else
        {
            authorization = await _gateway.AuthorizeWithCardAsync(payment.PayPalOrderId, card!, $"eshop-{payment.RequestKey}-authorize", cancellationToken);
        }

        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Authorization for order {OrderId} returned status {Status}", orderId, authorization.Status);
            throw new PaymentGatewayException(System.Net.HttpStatusCode.UnprocessableEntity, "AUTHORIZATION_NOT_CREATED",
                $"PayPal did not authorize the payment (status: {authorization.Status}). The card may have been declined; no funds were held.");
        }

        if (authorization.Amount != amount)
        {
            _logger.LogWarning("Authorization amount {Authorized} differs from order total {Total} for order {OrderId}", authorization.Amount, amount, orderId);
        }

        payment.MarkAuthorized(authorization.Id, authorization.Status, authorization.ExpirationTime);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.Fulfilled && payment is { Status: PaymentStatus.Captured })
        {
            // Idempotent retry: the capture already happened.
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment is not { Status: PaymentStatus.Authorized })
        {
            throw new InvalidOperationException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            // The hold has gone stale; renew it before capturing.
            GatewayAuthorization renewed;
            try
            {
                renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency, $"eshop-{payment.RequestKey}-reauthorize", cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new AuthorizationNotRenewableException(
                    $"The authorization hold for order {orderId} (authorization {payment.AuthorizationId}) has expired and PayPal could not renew it ({ex.ErrorName ?? ex.Message}). " +
                    "Do not fulfil this order yet: ask the shopper to pay again so a fresh hold is placed, then fulfil.", ex.DebugId);
            }
            payment.MarkAuthorizationRenewed(renewed.Status, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        var capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
            $"eshop-{payment.RequestKey}-capture", cancellationToken);

        if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(System.Net.HttpStatusCode.UnprocessableEntity, "CAPTURE_NOT_COMPLETED",
                $"PayPal did not complete the capture for order {orderId} (status: {capture.Status}). No funds were taken; retry fulfilment or investigate in the PayPal dashboard.");
        }

        payment.MarkCaptured(capture.Id, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<OrderPayment?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }
        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {orderId} is {order.Status} and can no longer be cancelled; issue a refund instead.");
        }

        if (payment is { Status: PaymentStatus.Authorized })
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId!, $"eshop-{payment.RequestKey}-void", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required for refunds.");
        }

        var keyedPayment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByRefundIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (keyedPayment is not null)
        {
            var existingRefund = keyedPayment.Refunds.First(r => r.IdempotencyKey == idempotencyKey);
            if (keyedPayment.OrderId != orderId)
            {
                throw new DuplicateException($"Idempotency key '{idempotencyKey}' was already used for a refund on order {keyedPayment.OrderId}.");
            }
            // Same key, same order: return the original refund without refunding again.
            return existingRefund;
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is not { Status: PaymentStatus.Captured })
        {
            throw new InvalidOperationException($"Order {orderId} has no captured payment to refund.");
        }

        var remaining = payment.RefundableAmount();
        var refundAmount = amount.HasValue ? Math.Round(amount.Value, 2) : remaining;
        if (refundAmount <= 0 || refundAmount > remaining)
        {
            throw new ArgumentException($"Refund amount must be between 0.01 and the remaining refundable amount {remaining:0.00} {payment.Currency}.");
        }

        var refund = await _gateway.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency, idempotencyKey, note, cancellationToken);

        var entity = payment.AddRefund(refund.Id, refundAmount, refund.Status, idempotencyKey, note);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkRefundApplied(payment.RefundableAmount() <= 0m);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return entity;
    }
}
