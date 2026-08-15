using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Serialize money operations per order so a double-click can never authorize or capture twice
    // within this process. PayPal-Request-Id idempotency guards the processor side as well.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order line must have a quantity of at least 1.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, string.IsNullOrEmpty(pictureUri) ? "eshop" : pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var shipTo = shipToAddress ?? new Address("123 Main St", "Redmond", "WA", "US", "98052");
        var order = new Order(buyerId, shipTo, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderSpecification(orderId), cancellationToken);
            if (existing is not null && existing.Status != PaymentStatus.Failed)
            {
                // Idempotent: a hold (or capture) already exists for this order.
                return existing;
            }
            if (existing is not null)
            {
                await _paymentRepository.DeleteAsync(existing, cancellationToken);
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.Cancelled)
            {
                throw new PaymentValidationException($"Order {orderId} is {order.Status} and can no longer be paid.");
            }

            var amount = new PaymentAmount(RoundMoney(order.Total()), Currency);
            var reference = orderId.ToString();

            AuthorizeResult result = instrument switch
            {
                { SavedPaymentMethodId: { } savedId } => await AuthorizeWithSavedCardAsync(buyerId, savedId, amount, reference, cancellationToken),
                { Card: { } card } => await _gateway.AuthorizeWithCardAsync(amount, reference, card, cancellationToken),
                _ => throw new PaymentValidationException("A card or a saved payment method must be supplied to pay.")
            };

            var payment = new Payment(orderId, buyerId, amount.Value, amount.Currency, result.PayPalOrderId);
            payment.SetAuthorized(result.Authorization.AuthorizationId, result.Authorization.Status, result.Authorization.ExpiresAt);
            order.MarkAuthorized();

            await _paymentRepository.AddAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation($"Order {orderId} authorized: {amount.Value} {amount.Currency}, authorization {result.Authorization.AuthorizationId}.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AuthorizeResult> AuthorizeWithSavedCardAsync(string buyerId, int savedId, PaymentAmount amount, string reference, CancellationToken cancellationToken)
    {
        var card = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(savedId, buyerId), cancellationToken)
            ?? throw new PaymentValidationException($"Saved payment method {savedId} was not found.");
        return await _gateway.AuthorizeWithVaultedCardAsync(amount, reference, card.VaultId, cancellationToken);
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
                ?? throw new OrderNotFoundException(orderId);
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderSpecification(orderId), cancellationToken)
                ?? throw new PaymentValidationException($"Order {orderId} has no authorized payment to fulfil.");

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return payment; // idempotent — already captured
            }
            if (order.Status != OrderStatus.Authorized || payment.Status != PaymentStatus.Authorized)
            {
                throw new PaymentValidationException($"Order {orderId} is {order.Status} and cannot be fulfilled; it must be authorized first.");
            }

            var amount = new PaymentAmount(payment.Amount, payment.Currency);

            // Renew a stale hold rather than failing the fulfilment outright.
            if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {orderId} is stale (expires {expiry:o}); reauthorizing.");
                var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, amount, cancellationToken);
                payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            }

            var capture = await _gateway.CaptureAsync(payment.AuthorizationId!, amount, $"eshop-capture-{payment.AuthorizationId}", cancellationToken);
            payment.SetCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            order.MarkFulfilled();

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation($"Order {orderId} fulfilled: captured {capture.GrossAmount} {capture.Currency}, fee {capture.PayPalFee}, net {capture.NetAmount}.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Payment?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
                ?? throw new OrderNotFoundException(orderId);
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderSpecification(orderId), cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return payment; // idempotent
            }
            if (order.Status == OrderStatus.Fulfilled)
            {
                throw new PaymentValidationException($"Order {orderId} has already been fulfilled; issue a refund instead of cancelling.");
            }

            if (payment is not null && payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
            {
                await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                payment.MarkVoided();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderSpecification(orderId), cancellationToken)
                ?? throw new PaymentValidationException($"Order {orderId} has no captured payment to refund.");

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            {
                throw new PaymentValidationException($"Order {orderId} is {payment.Status} and cannot be refunded; only a captured payment can be refunded.");
            }

            // Idempotency: replaying the same key must not refund twice.
            var already = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (already is not null)
            {
                return already;
            }

            var refundAmount = RoundMoney(amount ?? payment.RefundableAmount);
            if (refundAmount <= 0m)
            {
                throw new PaymentValidationException("Refund amount must be greater than zero.");
            }
            if (refundAmount > payment.RefundableAmount)
            {
                throw new PaymentValidationException(
                    $"Refund of {refundAmount} {payment.Currency} exceeds the refundable amount of {payment.RefundableAmount} {payment.Currency}.");
            }

            var result = await _gateway.RefundAsync(payment.CaptureId!, new PaymentAmount(refundAmount, payment.Currency), idempotencyKey, cancellationToken);
            var recordedAmount = result.Amount > 0m ? result.Amount : refundAmount;
            var refund = payment.AddRefund(result.RefundId, recordedAmount, result.Status, idempotencyKey);

            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation($"Order {orderId} refunded {recordedAmount} {payment.Currency} (refund {result.RefundId}); status now {payment.Status}.");
            return refund;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<OrderWithPayment> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderSpecification(orderId), cancellationToken);
        return new OrderWithPayment(order, payment);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new AllPaymentsSpecification(), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var inPayPalOnly = new List<GatewayTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            var payment = payments.FirstOrDefault(p => IsMatch(p, tx));
            if (payment is not null)
            {
                matched.Add(new ReconciliationMatch(tx.TransactionId, tx.Status, tx.Amount, tx.Currency, payment.OrderId, payment.Status.ToString()));
                matchedOrderIds.Add(payment.OrderId);
            }
            else
            {
                inPayPalOnly.Add(tx);
            }
        }

        var inEShopOnly = payments
            .Where(p => p.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            .Where(p => !matchedOrderIds.Contains(p.OrderId))
            .Select(p => new ReconciliationEShopOnly(p.OrderId, p.Status.ToString(), p.CaptureId, p.CapturedAmount))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, matched, inPayPalOnly, inEShopOnly);
    }

    private static bool IsMatch(Payment payment, GatewayTransaction tx)
    {
        if (!string.IsNullOrEmpty(tx.ReferenceId) && tx.ReferenceId == payment.OrderId.ToString())
        {
            return true;
        }
        if (!string.IsNullOrEmpty(tx.TransactionId))
        {
            if (tx.TransactionId == payment.CaptureId || tx.TransactionId == payment.AuthorizationId)
            {
                return true;
            }
            if (payment.Refunds.Any(r => r.PayPalRefundId == tx.TransactionId))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Do not distinguish "not found" from "not yours" — one shopper must never learn about another's orders.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
