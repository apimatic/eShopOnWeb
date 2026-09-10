using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private const string DefaultPictureUri = "eCatalog-item-default.png";

    // Stable within a process run (so a double-click reuses the same PayPal-Request-Id and does not act
    // twice) but unique across runs — the in-memory store resets order ids each run, so a fixed key would
    // otherwise collide with a previous run's request at PayPal and replay a stale result.
    private static readonly string RunToken = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IPaymentConfiguration _configuration;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
        IPayPalGateway gateway,
        IPaymentConfiguration configuration,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shipTo,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Every order line must have a quantity of at least 1.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}", nameof(lines));

        var orderItems = lines.Select(line =>
        {
            var item = byId[line.CatalogItemId];
            var pictureUri = string.IsNullOrEmpty(item.PictureUri) ? DefaultPictureUri : item.PictureUri;
            var itemOrdered = new CatalogItemOrdered(item.Id, item.Name, pictureUri);
            return new OrderItem(itemOrdered, item.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(order.Id, buyerId, _configuration.Currency, order.Total());
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Order {0} placed by {1} awaiting payment ({2} {3}).",
            order.Id, buyerId, payment.Amount, payment.Currency);
        return order.Id;
    }

    public async Task<OrderPaymentView> AuthorizeAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken ct)
    {
        if ((card is null) == (savedPaymentMethodId is null))
            throw new ArgumentException("Supply exactly one of a card or a saved payment method id.");

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent in effect: a double-click on an already-authorized order returns the existing hold.
        if (payment.Status == PaymentStatus.Authorized)
            return await BuildViewAsync(payment, ct);
        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentOperationException($"Order {orderId} cannot be authorized from state {payment.Status}.");

        CardPaymentInstrument instrument;
        if (savedPaymentMethodId is not null)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
            if (method is null || method.BuyerId != buyerId)
                throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);
            instrument = CardPaymentInstrument.Saved(method.PayPalVaultId);
        }
        else
        {
            instrument = CardPaymentInstrument.OneOff(card!);
        }

        var result = await _gateway.AuthorizeAsync(
            payment.Amount, Correlation(orderId), instrument, IdempotencyKey("auth", orderId), ct);

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {0} authorized (paypalOrder {1}, authorization {2}).",
            orderId, result.PayPalOrderId, result.AuthorizationId);
        return await BuildViewAsync(payment, ct);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        // Idempotent: fulfilling an already-fulfilled order returns its captured state.
        if (payment.Status == PaymentStatus.Captured
            || payment.Status == PaymentStatus.PartiallyRefunded
            || payment.Status == PaymentStatus.Refunded)
            return await BuildViewAsync(payment, ct);
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentOperationException($"Order {orderId} cannot be fulfilled from state {payment.Status}.");

        // A stale hold is renewed rather than failing the fulfilment outright.
        var renewal = await _gateway.EnsureCapturableAsync(
            payment.AuthorizationId!, payment.Amount, IdempotencyKey("reauth", orderId), ct);
        if (!renewal.CanCapture)
            throw new PaymentOperationException(
                $"Order {orderId} cannot be fulfilled: the authorization can no longer be renewed. {renewal.Reason}");
        if (renewal.Renewed)
        {
            payment.RenewAuthorization(renewal.AuthorizationId);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogWarning("Order {0} authorization renewed to {1} before capture.", orderId, renewal.AuthorizationId);
        }

        var capture = await _gateway.CaptureAsync(
            payment.AuthorizationId!, payment.Amount, Correlation(orderId), IdempotencyKey("capture", orderId), ct);

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PaypalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {0} fulfilled: captured {1} (fee {2}, net {3}), capture {4}.",
            orderId, capture.GrossAmount, capture.PaypalFee, capture.NetAmount, capture.CaptureId);
        return await BuildViewAsync(payment, ct);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        // Idempotent: cancelling an already-cancelled order returns its voided state.
        if (payment.Status == PaymentStatus.Voided)
            return await BuildViewAsync(payment, ct);
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentOperationException($"Order {orderId} cannot be cancelled from state {payment.Status}.");

        await _gateway.VoidAsync(payment.AuthorizationId!, IdempotencyKey("void", orderId), ct);

        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {0} cancelled; hold {1} released.", orderId, payment.AuthorizationId);
        return await BuildViewAsync(payment, ct);
    }

    public async Task<RefundView> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent: a repeat under the same key returns the refund already made, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return ToRefundView(payment, existing);

        var refundAmount = amount ?? payment.RefundableRemaining();
        payment.EnsureRefundable(refundAmount);   // never refundable beyond what was captured

        var result = await _gateway.RefundAsync(payment.CaptureId!, amount, idempotencyKey, ct);

        var refund = payment.AddRefund(result.RefundId, result.Amount, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {0} refunded {1} (refund {2}); total refunded {3}.",
            orderId, result.Amount, result.RefundId, payment.RefundedAmount);
        return ToRefundView(payment, refund);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var ordersById = orders.ToDictionary(o => o.Id);

        var views = new List<OrderPaymentView>(payments.Count);
        foreach (var payment in payments.OrderByDescending(p => p.OrderId))
        {
            ordersById.TryGetValue(payment.OrderId, out var order);
            views.Add(BuildView(payment, order));
        }
        return views;
    }

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw new OrderNotFoundException(orderId);
        return payment;
    }

    private async Task<Payment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);
        if (payment.BuyerId != buyerId)
            throw new OrderNotFoundException(orderId);   // don't leak another shopper's order
        return payment;
    }

    private async Task<OrderPaymentView> BuildViewAsync(Payment payment, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(payment.OrderId), ct);
        return BuildView(payment, order);
    }

    private static OrderPaymentView BuildView(Payment payment, Order? order)
    {
        var items = order?.OrderItems
            .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList() ?? new List<OrderLineView>();

        return new OrderPaymentView(
            payment.OrderId,
            payment.BuyerId,
            payment.Status.ToString(),
            payment.Currency,
            payment.Amount,
            payment.CapturedAmount,
            payment.PaypalFee,
            payment.NetAmount,
            payment.RefundedAmount,
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.CaptureId,
            order?.OrderDate ?? payment.CreatedDate,
            items);
    }

    private static RefundView ToRefundView(Payment payment, PaymentRefund refund) => new(
        refund.RefundId,
        payment.OrderId,
        refund.Amount,
        refund.Currency,
        payment.RefundedAmount,
        payment.Status.ToString());

    private static string Correlation(int orderId) => $"ESHOP-{orderId}";

    private static string IdempotencyKey(string op, int orderId) =>
        string.Create(CultureInfo.InvariantCulture, $"eshop-{op}-{orderId}-{RunToken}");
}
