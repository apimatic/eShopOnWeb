using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderOperationLock _operationLock;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IOrderOperationLock operationLock,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _operationLock = operationLock;
        _logger = logger;
        _currency = settings.ResolvedCurrency;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ValidationException("An order must contain at least one line.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new ValidationException("Every order line must have a quantity greater than zero.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Amounts come from catalog prices.
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed for buyer {buyerId} awaiting payment, total {order.Total():0.00} {_currency}.");
        return order;
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        if (instruction is null || !instruction.IsValid)
        {
            throw new ValidationException("Provide either card details or a saved card id (exactly one) to pay.");
        }

        using var _ = await _operationLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            _logger.LogInformation($"Order {orderId} already authorized (authorization {order.Payment.AuthorizationId}); returning existing hold.");
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new ConflictException($"Order {orderId} cannot be paid from status {order.Status}.");
        }

        var amount = order.Total();
        var requestId = $"auth-{order.PaymentReference}";

        AuthorizationResult result;
        if (instruction.UsesSavedCard)
        {
            var card = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(instruction.SavedCardId!.Value, buyerId), cancellationToken)
                ?? throw new NotFoundException($"Saved card {instruction.SavedCardId} was not found.");
            result = await _payPal.AuthorizeWithVaultedCardAsync(amount, _currency, card.VaultId, requestId, cancellationToken);
        }
        else
        {
            result = await _payPal.AuthorizeWithCardAsync(amount, _currency, instruction.Card!, requestId, cancellationToken);
        }

        order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, _currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized: PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId} ({result.Status}), hold {amount:0.00} {_currency}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);

        // Idempotent: capturing an already-fulfilled order does not capture twice.
        if (order.Status == OrderStatus.Fulfilled)
        {
            _logger.LogInformation($"Order {orderId} already fulfilled (capture {order.Payment?.CaptureId}); returning existing capture.");
            return order;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new ConflictException($"Order {orderId} cannot be fulfilled from status {order.Status}.");
        }

        var payment = order.Payment;
        var amount = payment.Amount;
        var authorizationId = payment.AuthorizationId;

        // Renew a stale authorization rather than failing the fulfilment outright.
        var current = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (IsStale(current.Status))
        {
            _logger.LogInformation($"Order {orderId} authorization {authorizationId} is {current.Status}; renewing before capture.");
            var renewed = await _payPal.ReauthorizeAsync(authorizationId, amount, _currency, cancellationToken); // throws PayPalAuthorizationUnrenewableException if it can't
            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status);
            authorizationId = renewed.AuthorizationId;
            _logger.LogInformation($"Order {orderId} authorization renewed to {authorizationId} ({renewed.Status}).");
        }
        else if (IsUncapturable(current.Status))
        {
            throw new ConflictException(
                $"Order {orderId} cannot be fulfilled: its PayPal authorization is {current.Status}. It must be re-placed and paid again.");
        }

        var capture = await _payPal.CaptureAuthorizationAsync(authorizationId, amount, _currency, $"capture-{order.PaymentReference}", cancellationToken);
        order.RecordFulfilment(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled: capture {capture.CaptureId} ({capture.Status}), captured {capture.GrossAmount:0.00}, fee {capture.PayPalFee:0.00}, net {capture.NetAmount:0.00} {_currency}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation($"Order {orderId} already cancelled; nothing to release.");
            return order;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new ConflictException($"Order {orderId} cannot be cancelled from status {order.Status}.");
        }

        await _payPal.VoidAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled: authorization {order.Payment.AuthorizationId} voided, hold released.");
        return order;
    }

    public async Task<(Order Order, Refund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ValidationException("A refund idempotency key is required.");
        }
        if (amount.HasValue && amount.Value <= 0m)
        {
            throw new ValidationException("Refund amount must be greater than zero.");
        }

        using var _ = await _operationLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status != OrderStatus.Fulfilled || order.Payment?.CaptureId is null)
        {
            throw new ConflictException($"Order {orderId} cannot be refunded from status {order.Status}; only captured (fulfilled) orders can be refunded.");
        }

        var payment = order.Payment;

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Order {orderId} refund idempotency key already applied (refund {existing.PayPalRefundId}); returning existing refund.");
            return (order, existing);
        }

        // A partly-refunded order must never become refundable beyond what was captured.
        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
        {
            throw new ConflictException($"Order {orderId} is fully refunded; nothing remains to refund.");
        }
        if (amount.HasValue && amount.Value > remaining)
        {
            throw new ValidationException($"Refund of {amount.Value:0.00} exceeds the refundable remaining balance of {remaining:0.00} {_currency}.");
        }

        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, amount, _currency, idempotencyKey, cancellationToken);
        var refundAmount = result.Amount > 0m ? result.Amount : (amount ?? remaining);

        var refund = new Refund(result.RefundId, idempotencyKey, refundAmount, result.Status);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded: refund {result.RefundId} ({result.Status}) for {refundAmount:0.00} {_currency}; remaining refundable {payment.RefundableRemaining:0.00}.");
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        // One shopper must never see or act on another's order — treat as not found to avoid leaking existence.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private static string OrderKey(int orderId) => $"order:{orderId}";

    // PayPal does not model an "EXPIRED" authorization enum member, but the wire may still send it.
    private static bool IsStale(string status) =>
        string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool IsUncapturable(string status) =>
        string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase);
}
