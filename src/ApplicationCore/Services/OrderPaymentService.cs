using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Unique per application run: PayPal merchant accounts reject duplicate invoice ids,
    // and the in-memory store restarts order ids from 1 on every run.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPalGateway,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPalGateway = payPalGateway;
        _payPalSettings = payPalSettings;
    }

    public async Task<PayOrderResult> PayAsync(int orderId, string buyerId, CardPaymentDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        // A shopper must never see or act on another shopper's order: behave as if it does not exist.
        if (order.BuyerId != buyerId)
        {
            throw new NotFoundException(orderId.ToString(), nameof(Order));
        }

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            var existing = await GetLatestPaymentAsync(orderId, cancellationToken);
            if (existing is { Status: PaymentStatus.Authorized })
            {
                return new PayOrderResult { Order = order, Payment = existing, AlreadyPaid = true };
            }
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be paid in its current state: {order.Status}.");
        }

        if ((card is null) == (savedCardId is null))
        {
            throw new PaymentValidationException("Provide exactly one payment source: card details or a savedCardId.");
        }

        var amount = order.Total();
        var currency = _payPalSettings.Currency;

        // Deterministic request id per payment attempt: a retried request collapses at PayPal
        // instead of placing a second hold, while a genuine re-pay after expiry is a new attempt.
        var attempt = await _paymentRepository.CountAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken) + 1;
        var requestId = $"eshop-{RunId}-order-{orderId}-authorize-{attempt}";
        var referenceId = $"eshop-{RunId}-order-{orderId}-{attempt}";

        PayPalAuthorizationResult authorization;
        int? usedSavedCardId = null;

        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new NotFoundException(savedCardId.Value.ToString(), nameof(SavedCard));
            }

            usedSavedCardId = savedCard.Id;
            authorization = await _payPalGateway.AuthorizeWithVaultedCardAsync(
                amount, currency, savedCard.VaultPaymentTokenId, referenceId, requestId, cancellationToken);
        }
        else
        {
            authorization = await _payPalGateway.AuthorizeWithCardAsync(
                amount, currency, card!, referenceId, requestId, cancellationToken);
        }

        var payment = new Payment(order.Id, buyerId, amount, currency,
            authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status,
            authorization.ExpiresAt, usedSavedCardId, authorization.CardBrand, authorization.CardLast4);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return new PayOrderResult { Order = order, Payment = payment };
    }

    public async Task<FulfilOrderResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await GetLatestPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled && payment is { Status: PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded })
        {
            return new FulfilOrderResult { Order = order, Payment = payment, AlreadyFulfilled = true };
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment is null || payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled in its current state: {order.Status}.");
        }

        var currency = payment.Currency;
        var renewed = false;

        try
        {
            if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= System.DateTimeOffset.UtcNow)
            {
                await RenewAuthorizationAsync(payment, currency, cancellationToken);
                renewed = true;
            }

            var capture = await _payPalGateway.CaptureAuthorizationAsync(
                payment.AuthorizationId, payment.Amount, currency,
                $"eshop-order-{orderId}-capture-{payment.AuthorizationId}", cancellationToken);

            payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        }
        catch (PayPalApiException captureFailure) when (!renewed && IsRenewableFailure(captureFailure))
        {
            // The hold went stale before fulfilment: renew it rather than failing outright.
            try
            {
                await RenewAuthorizationAsync(payment, currency, cancellationToken);
            }
            catch (PayPalApiException)
            {
                payment.MarkAuthorizationExpired();
                order.MarkPaymentRequired();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);

                throw new PaymentConflictException(
                    $"The PayPal authorization for order {orderId} has expired and can no longer be renewed. " +
                    $"The order has been moved back to 'PendingPayment'; ask the shopper to pay again via POST /api/orders/{orderId}/pay.");
            }

            renewed = true;
            var capture = await _payPalGateway.CaptureAuthorizationAsync(
                payment.AuthorizationId, payment.Amount, currency,
                $"eshop-order-{orderId}-capture-{payment.AuthorizationId}", cancellationToken);

            payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        }

        order.MarkFulfilled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new FulfilOrderResult { Order = order, Payment = payment, AuthorizationRenewed = renewed };
    }

    public async Task<CancelOrderResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await GetLatestPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return new CancelOrderResult { Order = order, Payment = payment, AlreadyCancelled = true };
        }

        // Void the hold first: if PayPal cannot release the funds, the order stays uncancelled.
        if (payment is { Status: PaymentStatus.Authorized })
        {
            await _payPalGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                $"eshop-order-{orderId}-void-{payment.AuthorizationId}", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new CancelOrderResult { Order = order, Payment = payment };
    }

    public async Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string? noteToPayer,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await GetLatestPaymentAsync(orderId, cancellationToken);

        if (payment is null || string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return new RefundOrderResult { Order = order, Payment = payment, Refund = existing, Replayed = true };
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m)
        {
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        }
        if (refundAmount > payment.RefundableAmount)
        {
            throw new PaymentConflictException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the remaining refundable amount of " +
                $"{payment.RefundableAmount:0.00} {payment.Currency} (captured {payment.CapturedAmount:0.00}, already refunded {payment.TotalRefunded:0.00}).");
        }

        var refund = await _payPalGateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency,
            noteToPayer, $"eshop-refund-{idempotencyKey}", cancellationToken);

        var recorded = payment.AddRefund(refund.RefundId, refund.Amount, idempotencyKey, refund.Status);
        order.MarkRefunded(payment.Status == PaymentStatus.Refunded);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new RefundOrderResult { Order = order, Payment = payment, Refund = recorded };
    }

    private async Task RenewAuthorizationAsync(Payment payment, string currency, CancellationToken cancellationToken)
    {
        var renewed = await _payPalGateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, currency,
            $"eshop-reauthorize-{payment.AuthorizationId}", cancellationToken);
        payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
    }

    private static bool IsRenewableFailure(PayPalApiException exception)
    {
        // A capture attempted against a stale/expired hold is rejected with a client error;
        // a server-side PayPal failure is not something reauthorizing would fix.
        return exception.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException(orderId.ToString(), nameof(Order));
        }
        return order;
    }

    private async Task<Payment?> GetLatestPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
    }
}
