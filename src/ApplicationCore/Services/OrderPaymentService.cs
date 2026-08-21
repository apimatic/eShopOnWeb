using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentService _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalPaymentService payPal,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _currency = settings.Value.Currency;
    }

    public async Task<Payment> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        var requested = lines?.Where(l => l.Quantity > 0).ToList() ?? new List<OrderLineRequest>();
        if (requested.Count == 0)
        {
            throw new PaymentFlowException("An order must contain at least one item with a positive quantity.");
        }

        var itemIds = requested.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in requested)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new PaymentFlowException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var invoiceReference = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new Payment(order.Id, buyerId, order.Total(), _currency, invoiceReference);
        payment = await _paymentRepository.AddAsync(payment, ct);

        return payment;
    }

    public async Task<Payment> AuthorizeAsync(int orderId, string buyerId, PayInstruction instruction,
        CancellationToken ct = default)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent in effect: a repeat of a completed authorization returns the existing hold.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }

        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentFlowException(
                $"Order {orderId} cannot be paid because its payment state is {payment.Status}.");
        }

        var (source, paymentMethodId) = await ResolvePaymentSourceAsync(instruction, buyerId, ct);

        // Persist the idempotency key BEFORE the first attempt so a retry reuses the same PayPal-Request-Id.
        var idempotencyKey = payment.EnsureAuthorizeIdempotencyKey();
        await _paymentRepository.UpdateAsync(payment, ct);

        var result = await _payPal.AuthorizeAsync(payment.Amount, payment.CurrencyCode, payment.InvoiceReference,
            payment.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture), source, idempotencyKey, ct);

        payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus,
            result.ExpiresAt, paymentMethodId);
        await _paymentRepository.UpdateAsync(payment, ct);

        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Fulfilled)
        {
            return payment; // already captured — idempotent
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentFlowException(
                $"Order {orderId} cannot be fulfilled because its payment state is {payment.Status}.");
        }

        var idempotencyKey = payment.EnsureCaptureIdempotencyKey();
        await _paymentRepository.UpdateAsync(payment, ct);

        var result = await _payPal.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
            payment.AuthorizationExpiresAt, idempotencyKey, ct);

        // The authorization may have been renewed (reauthorized) during capture.
        if (!string.Equals(result.AuthorizationId, payment.AuthorizationId, StringComparison.Ordinal))
        {
            payment.RecordReauthorization(result.AuthorizationId, "CAPTURED", result.ExpiresAt);
        }

        payment.RecordCapture(result.CaptureId, result.CaptureStatus, result.Gross, result.Fee, result.Net);
        await _paymentRepository.UpdateAsync(payment, ct);

        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // already cancelled — idempotent
        }

        if (payment.Status == PaymentStatus.AwaitingPayment)
        {
            // Nothing was ever held; cancel the unpaid order without calling PayPal.
            payment.RecordVoid("UNAUTHORIZED");
            await _paymentRepository.UpdateAsync(payment, ct);
            return payment;
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentFlowException(
                $"Order {orderId} cannot be cancelled because its payment state is {payment.Status}. " +
                "A fulfilled order must be refunded instead.");
        }

        var idempotencyKey = $"void-{payment.AuthorizationId}";
        await _payPal.VoidAsync(payment.AuthorizationId!, idempotencyKey, ct);

        payment.RecordVoid("VOIDED");
        await _paymentRepository.UpdateAsync(payment, ct);

        return payment;
    }

    public async Task<Payment> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent: repeating a refund request under the same key returns the same payment/refund unchanged.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return payment;
        }

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded)
            || string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentFlowException(
                $"Order {orderId} cannot be refunded because its payment state is {payment.Status}.");
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentFlowException($"Order {orderId} has no captured amount left to refund.");
        }

        var effectiveAmount = amount ?? remaining;
        if (effectiveAmount <= 0m)
        {
            throw new PaymentFlowException("A refund amount must be positive.");
        }

        if (effectiveAmount > remaining)
        {
            throw new PaymentFlowException(
                $"Refund of {effectiveAmount} exceeds the {remaining} still refundable on order {orderId}.");
        }

        // A whole-capture refund with no prior refunds is a full refund (no amount sent to PayPal).
        var isFullRefund = payment.RefundedAmount() == 0m && effectiveAmount == (payment.CapturedGross ?? payment.Amount);
        decimal? refundAmountToSend = isFullRefund ? (decimal?)null : effectiveAmount;

        var result = await _payPal.RefundAsync(payment.CaptureId!, refundAmountToSend, payment.CurrencyCode,
            idempotencyKey, ct);

        payment.AddRefund(result.RefundId, effectiveAmount, result.RefundStatus, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        return payment;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new OrdersByBuyerWithItemsSpec(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), ct);

        var paymentsByOrder = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedDate).First());

        var result = new List<OrderWithPayment>();
        foreach (var order in orders.OrderByDescending(o => o.Id))
        {
            if (paymentsByOrder.TryGetValue(order.Id, out var payment))
            {
                result.Add(new OrderWithPayment(order, payment));
            }
        }

        return result;
    }

    private async Task<(PaymentSourceInput source, int? paymentMethodId)> ResolvePaymentSourceAsync(
        PayInstruction instruction, string buyerId, CancellationToken ct)
    {
        if (instruction is null || (instruction.Card is null && instruction.PaymentMethodId is null))
        {
            throw new PaymentFlowException("Provide either card details or a saved card id to pay with.");
        }

        if (instruction.Card is not null && instruction.PaymentMethodId is not null)
        {
            throw new PaymentFlowException("Provide either card details or a saved card id, not both.");
        }

        if (instruction.PaymentMethodId is int methodId)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(methodId, ct);
            if (method is null || method.BuyerId != buyerId)
            {
                // Do not reveal another shopper's card; treat as not found.
                throw new PaymentFlowException($"Saved card {methodId} was not found.");
            }

            return (new PaymentSourceInput(null, method.PayPalVaultId), method.Id);
        }

        return (new PaymentSourceInput(instruction.Card, null), null);
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment is null)
        {
            throw new PaymentFlowException($"Order {orderId} was not found.");
        }

        return payment;
    }

    private async Task<Payment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        if (payment.BuyerId != buyerId)
        {
            // One shopper must never see or act on another's order; treat as not found.
            throw new PaymentFlowException($"Order {orderId} was not found.");
        }

        return payment;
    }
}
