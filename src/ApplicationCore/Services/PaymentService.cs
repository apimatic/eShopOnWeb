using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentSettings _settings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        PaymentSettings settings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.", 400);
        }

        // Merge duplicate lines so a catalog item quantity is summed, not double-counted.
        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        if (quantities.Values.Any(q => q <= 0))
        {
            throw new PaymentException("Item quantities must be greater than zero.", 400);
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        var missing = quantities.Keys.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", 400);
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent in effect: a repeat while already authorized returns the existing hold.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be paid because it is {payment.Status}.", 409);
        }

        CardDetails? card = null;
        string? vaultId = null;

        if (instruction.SavedPaymentMethodId is int methodId)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdForBuyerSpec(methodId, buyerId), cancellationToken);
            if (method is null)
            {
                throw new PaymentException("Saved card not found.", 404);
            }
            vaultId = method.VaultId;
        }
        else if (instruction.Card is not null)
        {
            card = instruction.Card;
        }
        else
        {
            throw new PaymentException("Provide card details or the id of a saved card.", 400);
        }

        GatewayAuthorization auth;
        try
        {
            auth = await _gateway.AuthorizeAsync(payment.AuthorizeIdempotencyKey, payment.Amount, Currency,
                card, vaultId, cancellationToken);
        }
        catch (PaymentException)
        {
            // A declined attempt must not poison the next one: bump the attempt so a retry
            // (possibly with a different card) uses a fresh idempotency key.
            payment.RecordAuthorizeFailure();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        payment.SetAuthorization(auth.PayPalOrderId, auth.AuthorizationId, auth.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Captured ||
            payment.Status == PaymentStatus.PartiallyRefunded ||
            payment.Status == PaymentStatus.Refunded)
        {
            // Already fulfilled — capturing again would take the money twice.
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be fulfilled because it is {payment.Status}.", 409);
        }

        var idempotencyKey = payment.CaptureIdempotencyKey;
        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAsync(idempotencyKey, payment.AuthorizationId, payment.Amount,
                Currency, finalCapture: true, cancellationToken);
        }
        catch (StaleAuthorizationException)
        {
            // The hold went stale before fulfilment: renew it, then capture the renewed hold.
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, Currency,
                cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            capture = await _gateway.CaptureAsync(idempotencyKey, payment.AuthorizationId, payment.Amount,
                Currency, finalCapture: true, cancellationToken);
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee,
            capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentException(
                $"Order {orderId} has been fulfilled and cannot be cancelled; refund it instead.", 409);
        }

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAsync(payment.AuthorizationId, cancellationToken);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent: the same key never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded)
            || payment.CaptureId is null || payment.CapturedAmount is null)
        {
            throw new PaymentException(
                $"Order {orderId} has no captured payment to refund.", 409);
        }

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.", 400);
        }
        if (refundAmount > payment.RefundableRemaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} exceeds the {payment.RefundableRemaining:0.00} " +
                $"still refundable on this order.", 409);
        }

        var refund = await _gateway.RefundAsync(idempotencyKey, payment.CaptureId, refundAmount, Currency,
            cancellationToken);

        var recorded = payment.AddRefund(refund.RefundId, refundAmount, refund.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => new OrderPaymentView(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // Treat another shopper's order as not found so existence is not leaked.
        if (order is null || order.BuyerId != buyerId)
        {
            throw new PaymentException($"Order {orderId} not found.", 404);
        }
        return order;
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentException($"No payment exists for order {orderId}.", 404);
        }
        return payment;
    }
}
