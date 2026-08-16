using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private const string DefaultPictureUri = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<(Order Order, OrderPayment Payment)> PlaceOrderAsync(string buyerId,
        IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? DefaultPictureUri : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var reference = BuildReconciliationReference(order.Id);
        var payment = new OrderPayment(order.Id, buyerId, order.Total(), Currency, reference);
        payment = await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId}; total {order.Total()} {Currency}; awaiting payment.");
        return (order, payment);
    }

    public async Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PaymentCard? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentException("Provide either card details or a saved card to pay with.");
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved card, not both.");
        }

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a repeated authorize does not place a second hold.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentException("This order was cancelled and can no longer be paid.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

        var lines = order.OrderItems
            .Select(i => new PaymentOrderLine(i.ItemOrdered.ProductName, i.Units, i.UnitPrice))
            .ToList();

        int? usedSavedCardId = null;
        string? vaultId = null;
        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(savedPaymentMethodId.Value, buyerId), cancellationToken)
                ?? throw new PaymentNotFoundException($"Saved card {savedPaymentMethodId} was not found.");
            vaultId = savedCard.PayPalVaultId;
            usedSavedCardId = savedCard.Id;
        }

        var request = new AuthorizeRequest(
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            ReconciliationReference: payment.ReconciliationReference,
            Lines: lines,
            Card: card,
            VaultId: vaultId,
            // Globally-unique yet stable across double-clicks (the reference carries a random suffix).
            IdempotencyKey: $"authorize-{payment.ReconciliationReference}");

        var result = await _gateway.AuthorizeAsync(request, cancellationToken);

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, usedSavedCardId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized; PayPal auth {result.AuthorizationId} status {result.Status}.");
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentByOrderIdAsync(orderId, cancellationToken)
            ?? throw new PaymentNotFoundException($"No payment found for order {orderId}.");

        if (payment.IsCaptured)
        {
            return payment; // idempotent: already fulfilled.
        }
        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentException("This order was cancelled and cannot be fulfilled.");
        }
        if (!payment.IsAuthorized)
        {
            throw new PaymentException("This order has not been paid (authorized) yet and cannot be fulfilled.");
        }

        var captureKey = $"capture-{payment.AuthorizationId}";
        CaptureResult result;
        try
        {
            result = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                captureKey, cancellationToken);
        }
        catch (AuthorizationExpiredException)
        {
            _logger.LogInformation($"Order {orderId}: authorization {payment.AuthorizationId} is stale; attempting to renew it.");
            AuthorizationResult renewed;
            try
            {
                renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                    payment.CurrencyCode, cancellationToken);
            }
            catch (PaymentException ex)
            {
                throw new PaymentException(
                    "The payment hold for this order has expired and can no longer be renewed. " +
                    "Ask the shopper to place and pay for the order again.", ex);
            }

            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            result = await _gateway.CaptureAsync(renewed.AuthorizationId, payment.Amount, payment.CurrencyCode,
                $"capture-{renewed.AuthorizationId}", cancellationToken);
        }

        payment.MarkCaptured(result.CaptureId, result.Status, result.GrossAmount, result.PayPalFee,
            result.NetAmount, result.CapturedAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} fulfilled; captured {result.GrossAmount} {result.CurrencyCode} " +
            $"(fee {result.PayPalFee}, net {result.NetAmount}); capture {result.CaptureId}.");
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentByOrderIdAsync(orderId, cancellationToken)
            ?? throw new PaymentNotFoundException($"No payment found for order {orderId}.");

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // idempotent.
        }
        if (payment.IsCaptured)
        {
            throw new PaymentException("This order has already been fulfilled; issue a refund instead of cancelling.");
        }

        if (payment.IsAuthorized)
        {
            await _gateway.VoidAsync(payment.AuthorizationId!, cancellationToken);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        if (!payment.IsCaptured)
        {
            throw new PaymentException("This order has not been fulfilled yet, so there is nothing to refund.");
        }

        // Idempotent in effect: repeating a refund under the same key returns the original refund.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentException("This order has already been fully refunded.");
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund amount {refundAmount:0.00} exceeds the refundable balance {remaining:0.00} " +
                "(captured amount minus what has already been refunded).");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode,
            idempotencyKey, payment.ReconciliationReference, noteToPayer, cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, result.Amount, result.RefundId, result.Status, noteToPayer);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} refunded {result.Amount} {result.CurrencyCode}; refund {result.RefundId} status {result.Status}.");
        return refund;
    }

    public async Task<OrderPayment?> GetPaymentByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentByOrderId = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => new OrderWithPayment(o, paymentByOrderId.GetValueOrDefault(o.Id)))
            .OrderByDescending(x => x.Order.Id)
            .ToList();
    }

    private async Task<OrderPayment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await GetPaymentByOrderIdAsync(orderId, cancellationToken);
        if (payment is null || !string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not distinguish "not found" from "not yours" — never leak another shopper's data.
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private static string BuildReconciliationReference(int orderId)
    {
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 12);
        return $"ESHOP-{orderId.ToString(CultureInfo.InvariantCulture)}-{shortId}";
    }
}
