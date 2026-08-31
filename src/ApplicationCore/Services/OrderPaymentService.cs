using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipToAddress =
        new Address("123 Main St", "Anytown", "CA", "US", "12345");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(items, nameof(items));
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Every order line must have a quantity of at least 1.", nameof(items));
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new EntityNotFoundException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, PayPalCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedPaymentMethodId is null)
        {
            throw new ArgumentException("Provide either card details or a savedPaymentMethodId.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetActivePaymentForOrderAsync(orderId, cancellationToken);

        // Idempotency: an order that already holds funds returns its existing authorization.
        if (payment is not null && payment.Status == Payment.Statuses.Authorized)
        {
            return payment;
        }
        if (payment is not null && payment.Status == Payment.Statuses.Captured)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        string? vaultTokenId = null;
        string? brand = null;
        string? last4 = null;
        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new EntityNotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");
            }
            vaultTokenId = savedCard.VaultTokenId;
            brand = savedCard.Brand;
            last4 = savedCard.Last4;
        }

        if (payment is null)
        {
            payment = new Payment(order.Id, buyerId, order.Total(), _payPalSettings.Currency);
            await _paymentRepository.AddAsync(payment, cancellationToken);
            payment.AssignRequestIds();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        try
        {
            if (payment.PayPalOrderId is null)
            {
                var created = await _payPalClient.CreateOrderAsync(
                    payment.AuthorizedAmount, payment.Currency, order.Id.ToString(),
                    card, vaultTokenId, payment.CreateRequestId, cancellationToken);

                if (string.Equals(created.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentException(
                        "PayPal requires the shopper to approve this payment in a browser " +
                        "(the card issued a 3D Secure challenge). This integration does not support an approval round-trip.");
                }

                payment.MarkOrderCreated(created.Id,
                    brand ?? created.CardBrand,
                    last4 ?? created.CardLastDigits ?? Last4Of(card),
                    savedPaymentMethodId);

                // Direct card payments are authorized inline by the create-order call.
                if (created.Authorization is not null)
                {
                    payment.MarkAuthorized(created.Authorization.Id, created.Authorization.Status);
                    order.MarkPaymentAuthorized();
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    return payment;
                }
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }

            var authorization = await _payPalClient.AuthorizeOrderAsync(
                payment.PayPalOrderId, payment.AuthorizeRequestId, cancellationToken);

            payment.MarkAuthorized(authorization.Id, authorization.Status);
            order.MarkPaymentAuthorized();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        catch (PayPalApiException ex)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException(
                $"PayPal could not authorize the payment for order {orderId}: {ex.ErrorName ?? "ERROR"} - {ex.Message}", ex);
        }
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetActivePaymentForOrderAsync(orderId, cancellationToken);

        // Idempotency: fulfilling an already-captured order returns the recorded capture.
        if (payment is not null && payment.Status == Payment.Statuses.Captured)
        {
            return payment;
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException($"Order {orderId} is cancelled and cannot be fulfilled.");
        }
        if (payment is null || payment.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} has not been paid; there is no authorization to capture.");
        }

        var authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.MarkAuthorizationRenewed(authorization.Status);

        var capturable = authorization.Status is "CREATED" or "PENDING";
        if (capturable)
        {
            try
            {
                var capture = await CaptureAsync(payment, cancellationToken);
                await CompleteCaptureAsync(order, payment, capture, cancellationToken);
                return payment;
            }
            catch (PayPalApiException)
            {
                // The hold went stale between the status check and the capture; renew below.
            }
        }

        try
        {
            var renewed = await _payPalClient.ReauthorizeAuthorizationAsync(
                payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency,
                payment.NextReauthorizeRequestId(), cancellationToken);
            payment.MarkAuthorizationRenewed(renewed.Status);
        }
        catch (PayPalApiException ex)
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException(
                $"The PayPal authorization {payment.AuthorizationId} for order {orderId} is {authorization.Status} " +
                $"and can no longer be renewed ({ex.ErrorName ?? "ERROR"} - {ex.Message}). " +
                "Cancel this order and ask the shopper to place and pay for a new one.", ex);
        }

        try
        {
            var captureAfterRenewal = await CaptureAsync(payment, cancellationToken);
            await CompleteCaptureAsync(order, payment, captureAfterRenewal, cancellationToken);
            return payment;
        }
        catch (PayPalApiException ex)
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException(
                $"PayPal could not capture the renewed authorization for order {orderId}: " +
                $"{ex.ErrorName ?? "ERROR"} - {ex.Message}. Retry the fulfilment or cancel the order.", ex);
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        // Idempotency: cancelling twice is a no-op.
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException(
                $"Order {orderId} has already been fulfilled; issue a refund instead of cancelling.");
        }

        var payment = await GetActivePaymentForOrderAsync(orderId, cancellationToken);
        if (payment is not null && payment.AuthorizationId is not null && payment.CaptureId is null)
        {
            try
            {
                await _payPalClient.VoidAuthorizationAsync(
                    payment.AuthorizationId, payment.VoidRequestId(), cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentException(
                    $"PayPal could not release the held funds (authorization {payment.AuthorizationId}) for order {orderId}: " +
                    $"{ex.ErrorName ?? "ERROR"} - {ex.Message}. Retry the cancellation.", ex);
            }
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Payment Payment, PaymentRefund Refund, bool AlreadyExisted)> RefundOrderAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetActivePaymentForOrderAsync(orderId, cancellationToken);
        if (payment is null || payment.CaptureId is null || !payment.CapturedAmount.HasValue)
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return (payment, existing, true);
        }

        var refundAmount = amount ?? payment.RemainingRefundable;
        if (refundAmount <= 0)
        {
            throw new PaymentException($"Order {orderId} has already been refunded in full.");
        }
        if (refundAmount > payment.RemainingRefundable)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the remaining refundable amount " +
                $"of {payment.RemainingRefundable:0.00} {payment.Currency} for order {orderId}.");
        }

        var requestId = payment.RefundRequestId(idempotencyKey);
        PayPalRefundInfo refund;
        try
        {
            refund = await _payPalClient.RefundCaptureAsync(
                payment.CaptureId, refundAmount, payment.Currency, requestId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"PayPal could not refund order {orderId}: {ex.ErrorName ?? "ERROR"} - {ex.Message}.", ex);
        }

        var entity = payment.AddRefund(refund.Id, refund.Amount ?? refundAmount, refund.Status, idempotencyKey);
        order.MarkRefunded(payment.TotalRefunded >= payment.CapturedAmount.Value);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (payment, entity, false);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new OrdersByBuyerWithItemsSpec(buyerId), cancellationToken);
    }

    public async Task<Payment?> GetActivePaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new ActivePaymentForOrderSpec(orderId), cancellationToken);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new ArgumentException("The 'to' date-time must be after the 'from' date-time.");
        }

        var transactions = await _payPalClient.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsInRangeSpec(from, to), cancellationToken);

        var byPayPalId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId is not null) byPayPalId.TryAdd(payment.AuthorizationId, payment);
            if (payment.CaptureId is not null) byPayPalId.TryAdd(payment.CaptureId, payment);
            foreach (var refund in payment.Refunds)
            {
                byPayPalId.TryAdd(refund.PayPalRefundId, payment);
            }
        }

        var report = new ReconciliationReport { From = from, To = to };
        var matchedPaymentIds = new HashSet<int>();
        foreach (var transaction in transactions)
        {
            Payment? matchedPayment = null;
            if (transaction.TransactionId is not null)
            {
                byPayPalId.TryGetValue(transaction.TransactionId, out matchedPayment);
            }
            if (matchedPayment is not null)
            {
                matchedPaymentIds.Add(matchedPayment.Id);
            }
            report.Transactions.Add(new ReconciledTransaction
            {
                TransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Time = transaction.Time,
                OrderId = matchedPayment?.OrderId,
                PaymentId = matchedPayment?.Id
            });
        }

        foreach (var payment in payments.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            report.PaymentsMissingFromPayPal.Add(new UnmatchedPayment
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                Amount = payment.CapturedAmount ?? payment.AuthorizedAmount,
                Currency = payment.Currency,
                CreatedAt = payment.CreatedAt
            });
        }

        return report;
    }

    private async Task<PayPalCaptureInfo> CaptureAsync(Payment payment, CancellationToken cancellationToken)
    {
        var capture = await _payPalClient.CaptureAuthorizationAsync(
            payment.AuthorizationId!, payment.AuthorizedAmount, payment.Currency,
            payment.NextCaptureRequestId(), cancellationToken);

        // The capture response omits the fee breakdown; fetch the capture details
        // so the payment records what PayPal reported (gross, fee, net).
        var details = await _payPalClient.GetCaptureAsync(capture.Id, cancellationToken);
        return details with
        {
            Status = string.IsNullOrEmpty(details.Status) ? capture.Status : details.Status,
            GrossAmount = details.GrossAmount != 0m ? details.GrossAmount : capture.GrossAmount
        };
    }

    private async Task CompleteCaptureAsync(Order order, Payment payment, PayPalCaptureInfo capture,
        CancellationToken cancellationToken)
    {
        payment.MarkCaptured(capture.Id, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
    }

    private static string? Last4Of(PayPalCardDetails? card)
    {
        if (card?.Number is null || card.Number.Length < 4)
        {
            return null;
        }
        return card.Number[^4..];
    }
}
