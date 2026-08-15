using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        PayPalOptions options,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _catalogItemRepository = catalogItemRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _options = options;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");

        // Consolidate duplicate lines and reject non-positive quantities.
        var wanted = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            wanted[line.CatalogItemId] = wanted.TryGetValue(line.CatalogItemId, out var q) ? q + line.Quantity : line.Quantity;
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(wanted.Keys.ToArray()), cancellationToken);

        var missing = wanted.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = wanted.Select(kv =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kv.Key);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri)) pictureUri = "no-image";
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(ordered, catalogItem.Price, kv.Value);
        }).ToList();

        var order = new Order(buyerId, CreateDefaultAddress(), items);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), _options.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {0} for {1} total {2} {3}", order.Id, buyerId, payment.Amount, payment.CurrencyCode);
        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentForBuyerAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a hold already placed (or money already taken) is returned unchanged.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentStateException($"Order {orderId} cannot be paid from its current state ({payment.Status}).");

        // Resolve exactly one funding source: raw card OR a saved card owned by the buyer.
        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            if (card is not null)
                throw new PaymentValidationException("Provide either card details or a saved card, not both.");

            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdForBuyerSpecification(paymentMethodId.Value, buyerId), cancellationToken);
            if (method is null)
                throw new PaymentNotFoundException($"Saved card {paymentMethodId.Value} was not found for this shopper.");
            vaultId = method.VaultId;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("Card details or a saved card id are required to pay.");
        }

        var instruction = new AuthorizeInstruction(
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            CustomId: payment.Reference.ToString("N"),
            InvoiceId: BuildInvoiceId(payment),
            IdempotencyKey: payment.AuthorizeRequestId,
            Card: card,
            VaultId: vaultId);

        AuthorizationResult result;
        try
        {
            result = await _payPal.AuthorizeAsync(instruction, cancellationToken);
        }
        catch (PaymentException)
        {
            // A decline leaves the order awaiting payment so the shopper can retry (with a fresh
            // idempotency key so the retry is not an idempotent replay of the decline).
            payment.RotateAuthorizeRequestId();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, paymentMethodId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Authorized order {0}: paypalOrder={1} auth={2} status={3}", orderId, result.PayPalOrderId, result.AuthorizationId, result.Status);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent: an already-captured order is returned unchanged.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return payment;
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException($"Order {orderId} must be authorized before it can be fulfilled (current state: {payment.Status}).");

        // Renew a hold that has already gone stale before attempting to capture.
        if (payment.IsAuthorizationStale(DateTimeOffset.UtcNow))
            await RenewAuthorizationAsync(payment, cancellationToken);

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(payment.AuthorizationId!, payment.CurrencyCode, payment.CaptureRequestId, cancellationToken);
        }
        catch (AuthorizationNotCapturableException)
        {
            // The hold went stale between our check and the capture — renew once, then capture again.
            await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _payPal.CaptureAsync(payment.AuthorizationId!, payment.CurrencyCode, payment.CaptureRequestId, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation($"Fulfilled order {orderId}: capture={capture.CaptureId} gross={capture.CapturedAmount} fee={capture.PayPalFee} net={capture.NetAmount}");
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
            return payment; // idempotent
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw new PaymentStateException($"Order {orderId} has already been fulfilled and can only be refunded, not cancelled.");
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException($"Order {orderId} has no active authorization to cancel (current state: {payment.Status}).");

        var result = await _payPal.VoidAsync(payment.AuthorizationId!, payment.VoidRequestId, cancellationToken);
        payment.MarkCancelled(result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Cancelled order {0}: authorization {1} voided", orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<Payment> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadPaymentForBuyerAsync(orderId, buyerId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            || payment.CaptureId is null)
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund (current state: {payment.Status}).");

        // Idempotency: a refund already recorded under this key is returned without refunding again.
        if (payment.HasRefundWithKey(idempotencyKey))
            return payment;

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
            throw new PaymentStateException($"Order {orderId} has already been fully refunded.");

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new PaymentValidationException($"Refund of {refundAmount} exceeds the {remaining} still refundable on order {orderId}.");

        var result = await _payPal.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode, idempotencyKey, cancellationToken);
        payment.AddRefund(idempotencyKey, result.RefundId, result.Amount, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Refunded order {0}: refund={1} amount={2} status={3}", orderId, result.RefundId, result.Amount, result.Status);
        return payment;
    }

    public async Task<IReadOnlyList<OrderPaymentSnapshot>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderPaymentSnapshot(o, paymentsByOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<Payment?> GetPaymentForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null || !string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            return null;
        return payment;
    }

    private async Task RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Renewing stale authorization {0} on order {1}", payment.AuthorizationId!, payment.OrderId);
        var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, Guid.NewGuid(), cancellationToken);
        payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
            throw new PaymentNotFoundException($"No payment exists for order {orderId}.");
        return payment;
    }

    private async Task<Payment> LoadPaymentForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            // Do not reveal existence of another shopper's order.
            throw new PaymentNotFoundException($"No payment exists for order {orderId}.");
        return payment;
    }

    // PayPal invoice_id (max 127 chars). Human-readable order reference made globally unique with the
    // per-payment reference, since some PayPal accounts require a unique invoice id per transaction.
    private static string BuildInvoiceId(Payment payment) => $"ESHOP-ORDER-{payment.OrderId}-{payment.Reference:N}";

    private static Address CreateDefaultAddress() =>
        new Address("N/A (API order)", "N/A", "N/A", "N/A", "00000");
}
