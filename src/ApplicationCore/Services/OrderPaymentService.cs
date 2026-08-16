using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // decimal comparison tolerance for money (half a cent)
    private const decimal Epsilon = 0.005m;

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPal,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PayPalCardDetails? card, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentException("Provide either card details or a saved card id to pay with.", PaymentErrorReason.Validation);
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved card id, not both.", PaymentErrorReason.Validation);
        }

        var order = await GetOrderForBuyerAsync(buyerId, orderId);

        // Idempotent: a double-click never authorizes twice.
        if (order.Status == OrderStatus.Authorized && order.Payment is { IsAuthorized: true })
        {
            _logger.LogInformation($"Order {orderId} is already authorized ({order.Payment.AuthorizationId}); returning existing authorization.");
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid because it is {order.Status}.", PaymentErrorReason.Conflict);
        }

        var amount = order.Total();
        var currency = _payPal.Currency;

        string? vaultId = null;
        if (savedPaymentMethodId is int pmId)
        {
            var pm = await _savedCardRepository.GetByIdAsync(pmId);
            if (pm is null || pm.BuyerId != buyerId)
            {
                throw new PaymentException("Saved card not found.", PaymentErrorReason.NotFound);
            }
            vaultId = pm.PayPalVaultId;
        }

        var invoiceId = PaymentReference.InvoiceId(order.Id, order.PaymentIntentId);
        var request = new PayPalAuthorizeRequest(
            Amount: amount,
            Currency: currency,
            InvoiceId: invoiceId,
            CustomId: order.Id.ToString(),
            Card: card,
            VaultId: vaultId);

        var result = await _payPal.AuthorizeAsync(request, idempotencyKey: $"auth-{order.PaymentIntentId}");

        if (result.RequiresBuyerAction)
        {
            throw new PaymentException(
                "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). This integration does not support a browser approval round-trip.",
                PaymentErrorReason.RequiresBuyerAction);
        }

        var payment = new OrderPayment(currency, amount, invoiceId);
        payment.SetAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        order.MarkAuthorized(payment);

        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation($"Order {orderId} authorized: paypalOrder={result.PayPalOrderId} auth={result.AuthorizationId} amount={amount:F2} {currency}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId)
    {
        var order = await LoadOrderAsync(orderId);

        if (order.Status == OrderStatus.Paid)
        {
            return order; // already fulfilled/captured — idempotent
        }
        if (order.Status != OrderStatus.Authorized || order.Payment is not { IsAuthorized: true })
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled because it is {order.Status}.", PaymentErrorReason.Conflict);
        }

        var payment = order.Payment!;
        var amount = payment.Amount;
        var currency = payment.Currency;
        var authId = payment.AuthorizationId!;

        PayPalCaptureResult capture;
        var knownExpired = payment.AuthorizationExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow.AddMinutes(-1);

        if (knownExpired)
        {
            _logger.LogWarning($"Order {orderId} authorization {authId} expired at {payment.AuthorizationExpiresAt}; renewing before capture.");
            authId = await RenewAuthorizationAsync(order, payment, amount, currency);
            capture = await _payPal.CaptureAuthorizationAsync(authId, amount, currency, CaptureKey(order, authId));
        }
        else
        {
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(authId, amount, currency, CaptureKey(order, authId));
            }
            catch (PayPalApiException ex) when (ex.IsAuthorizationStale)
            {
                _logger.LogWarning($"Order {orderId} capture on {authId} reported stale authorization ({ex.Issue}); renewing.");
                authId = await RenewAuthorizationAsync(order, payment, amount, currency);
                capture = await _payPal.CaptureAuthorizationAsync(authId, amount, currency, CaptureKey(order, authId));
            }
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount, capture.CapturedAt);
        order.MarkPaid();
        await _orderRepository.UpdateAsync(order);

        _logger.LogInformation($"Order {orderId} fulfilled: capture={capture.CaptureId} gross={capture.CapturedAmount:F2} fee={capture.PayPalFee:F2} net={capture.NetAmount:F2} {currency}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var order = await LoadOrderAsync(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status == OrderStatus.Paid)
        {
            throw new PaymentException($"Order {orderId} has already been fulfilled; use a refund to return money.", PaymentErrorReason.Conflict);
        }

        if (order.Status == OrderStatus.Authorized && order.Payment is { IsAuthorized: true } payment)
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!);
            payment.UpdateAuthorizationStatus("VOIDED");
            _logger.LogInformation($"Order {orderId} authorization {payment.AuthorizationId} voided; held funds released.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOrderForBuyerAsync(buyerId, orderId);

        if (order.Status != OrderStatus.Paid || order.Payment is not { IsCaptured: true } payment)
        {
            throw new PaymentException($"Order {orderId} cannot be refunded because it has not been fulfilled.", PaymentErrorReason.Conflict);
        }

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Order {orderId} refund idempotency key already applied; returning existing refund {existing.RefundId}.");
            return (order, existing);
        }

        var remaining = payment.RefundableRemaining;
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.", PaymentErrorReason.Validation);
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining + Epsilon)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:F2} exceeds the {remaining:F2} still refundable on order {orderId} (captured {payment.CapturedAmount:F2}, already refunded {payment.RefundedAmount:F2}).",
                PaymentErrorReason.Conflict);
        }

        var requestId = $"refund-{order.PaymentIntentId}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency, requestId, noteToPayer);
        var refund = payment.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status, DateTimeOffset.Now);

        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation($"Order {orderId} refunded {result.Amount:F2} {payment.Currency}: refund={result.RefundId} status={result.Status}.");
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
    }

    public async Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId)
    {
        var order = await LoadOrderAsync(orderId);
        // Do not reveal another shopper's order — treat as not found.
        if (order.BuyerId != buyerId)
        {
            throw new PaymentException($"Order {orderId} not found.", PaymentErrorReason.NotFound);
        }
        return order;
    }

    private async Task<Order> LoadOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsAndPaymentByIdSpec(orderId));
        if (order is null)
        {
            throw new PaymentException($"Order {orderId} not found.", PaymentErrorReason.NotFound);
        }
        return order;
    }

    private async Task<string> RenewAuthorizationAsync(Order order, OrderPayment payment, decimal amount, string currency)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, amount, currency, $"reauth-{order.PaymentIntentId}-{payment.AuthorizationId}");
            payment.SetAuthorization(payment.PayPalOrderId!, reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            _logger.LogInformation($"Order {order.Id} authorization renewed: new auth={reauth.AuthorizationId}.");
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The payment authorization for order {order.Id} has expired and can no longer be renewed (PayPal: {ex.Issue ?? "unavailable"}). Ask the shopper to place and pay for a new order.",
                PaymentErrorReason.Conflict, ex);
        }
    }

    private static string CaptureKey(Order order, string authId) => $"capture-{order.PaymentIntentId}-{authId}";
}
