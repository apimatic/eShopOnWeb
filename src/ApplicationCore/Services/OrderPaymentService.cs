using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Drives an order through pay (authorize) / fulfil (capture) / cancel (void) / refund against the
/// payment gateway, keeping the order's PayPal-owned state persisted so any later request can act on it.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentOptions _paymentOptions;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IPaymentOptions paymentOptions)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _paymentOptions = paymentOptions;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new InvalidOrderStateException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidOrderStateException("Every order line must have a quantity of at least one.");

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOrderStateException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a repeated pay for an already-authorized order returns the current state.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
            return order;
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new InvalidOrderStateException($"Order {orderId} cannot be paid because it is {order.Status}.");

        var request = await BuildAuthorizationRequestAsync(order, buyerId, instruction, cancellationToken);
        var result = await _paymentGateway.AuthorizeAsync(request, cancellationToken);

        var payment = new Payment(result.PayPalOrderId, result.AuthorizationId, result.Status, order.Total(), request.Currency);
        order.SetAuthorizedPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<AuthorizationRequest> BuildAuthorizationRequestAsync(Order order, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken)
    {
        Guard.Against.Null(instruction, nameof(instruction));

        var common = new
        {
            Amount = order.Total(),
            Currency = _paymentOptions.Currency,
            OrderReference = order.Id.ToString(),
            IdempotencyKey = $"order-{order.Id}-authorize"
        };

        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var savedCard = await _paymentMethodRepository.GetByIdAsync(savedId, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
                throw new PaymentMethodNotFoundException(savedId);

            return new AuthorizationRequest
            {
                Amount = common.Amount,
                Currency = common.Currency,
                OrderReference = common.OrderReference,
                IdempotencyKey = common.IdempotencyKey,
                VaultTokenId = savedCard.CardId
            };
        }

        if (instruction.Card is not null)
        {
            return new AuthorizationRequest
            {
                Amount = common.Amount,
                Currency = common.Currency,
                OrderReference = common.OrderReference,
                IdempotencyKey = common.IdempotencyKey,
                Card = instruction.Card
            };
        }

        throw new InvalidOrderStateException("Provide either card details or a saved card id to pay.");
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
            return order; // idempotent
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment?.AuthorizationId is null)
            throw new InvalidOrderStateException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");

        var payment = order.Payment;
        var captureKey = $"order-{orderId}-capture";

        CaptureResult capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId!, captureKey, cancellationToken);
        }
        catch (AuthorizationExpiredException)
        {
            // The hold went stale before fulfilment — renew it rather than failing the fulfilment outright.
            // ReauthorizeAsync throws an operator-actionable PaymentGatewayException if it can no longer be renewed.
            var reauth = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency, cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status);
            capture = await _paymentGateway.CaptureAsync(reauth.AuthorizationId, $"{captureKey}-reauth", cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent
        if (order.Status == OrderStatus.Fulfilled)
            throw new InvalidOrderStateException($"Order {orderId} is already fulfilled; issue a refund instead of cancelling.");

        // Release any held funds. If nothing was authorized yet there is nothing to void.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.AuthorizationId is not null)
        {
            await _paymentGateway.VoidAuthorizationAsync(order.Payment.AuthorizationId!, cancellationToken);
            order.Payment.MarkAuthorizationVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        var payment = order.Payment;
        if (order.Status != OrderStatus.Fulfilled || payment is null || !payment.IsCaptured)
            throw new InvalidOrderStateException($"Order {orderId} has not been fulfilled, so there is nothing to refund.");

        // Idempotent: repeating a refund under the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return (order, existing);

        if (payment.RefundableAmount <= 0m)
            throw new InvalidOrderStateException($"Order {orderId} has already been fully refunded.");

        // A null amount means "refund what's left"; a value is a partial refund. Never beyond the captured total.
        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m)
            throw new InvalidOrderStateException("Refund amount must be greater than zero.");
        if (refundAmount > payment.RefundableAmount)
            throw new InvalidOrderStateException(
                $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount of {payment.RefundableAmount:0.00}.");

        var result = await _paymentGateway.RefundAsync(new RefundRequest
        {
            CaptureId = payment.CaptureId!,
            Amount = refundAmount,
            Currency = payment.Currency,
            IdempotencyKey = idempotencyKey
        }, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpecification(from, to), cancellationToken);

        // Index eShop payments by every PayPal id they carry, and by eShop order id (custom_field correlation).
        var orderByPayPalId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var orderByReference = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            orderByReference[order.Id.ToString()] = order;
            var p = order.Payment!;
            foreach (var id in new[] { p.PayPalOrderId, p.AuthorizationId, p.CaptureId })
            {
                if (!string.IsNullOrEmpty(id))
                    orderByPayPalId[id!] = order;
            }
            foreach (var r in p.Refunds)
                orderByPayPalId[r.RefundId] = order;
        }

        var entries = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            Order? matched = null;
            if (!string.IsNullOrEmpty(txn.TransactionId) && orderByPayPalId.TryGetValue(txn.TransactionId, out var byId))
                matched = byId;
            else if (!string.IsNullOrEmpty(txn.ReferenceId) && orderByReference.TryGetValue(txn.ReferenceId!, out var byRef))
                matched = byRef;

            if (matched is not null)
            {
                matchedOrderIds.Add(matched.Id);
                entries.Add(new ReconciliationEntry
                {
                    Match = ReconciliationMatch.Matched,
                    OrderId = matched.Id,
                    PayPalOrderId = matched.Payment!.PayPalOrderId,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalTransactionStatus = txn.Status,
                    EShopAmount = matched.Payment.CapturedAmount ?? matched.Payment.Amount,
                    PayPalAmount = txn.Amount,
                    Currency = txn.Currency ?? matched.Payment.Currency,
                    Date = txn.Date
                });
            }
            else
            {
                entries.Add(new ReconciliationEntry
                {
                    Match = ReconciliationMatch.PayPalOnly,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalTransactionStatus = txn.Status,
                    PayPalAmount = txn.Amount,
                    Currency = txn.Currency,
                    Date = txn.Date,
                    PayPalOrderId = txn.ReferenceId
                });
            }
        }

        foreach (var order in orders.Where(o => !matchedOrderIds.Contains(o.Id)))
        {
            entries.Add(new ReconciliationEntry
            {
                Match = ReconciliationMatch.EShopOnly,
                OrderId = order.Id,
                PayPalOrderId = order.Payment!.PayPalOrderId,
                PayPalTransactionId = order.Payment.CaptureId,
                EShopAmount = order.Payment.CapturedAmount ?? order.Payment.Amount,
                Currency = order.Payment.Currency,
                Date = order.OrderDate
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count,
            EShopPaymentCount = orders.Count,
            MatchedCount = entries.Count(e => e.Match == ReconciliationMatch.Matched),
            PayPalOnlyCount = entries.Count(e => e.Match == ReconciliationMatch.PayPalOnly),
            EShopOnlyCount = entries.Count(e => e.Match == ReconciliationMatch.EShopOnly),
            Entries = entries
        };
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
            throw new OrderNotFoundException(orderId);
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        // "Belongs to another shopper" is reported as not-found — never reveal another shopper's order.
        if (order is null || order.BuyerId != buyerId)
            throw new OrderNotFoundException(orderId);
        return order;
    }
}
