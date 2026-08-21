using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _settings = settings;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
            throw new PaymentException("An order must contain at least one item.");

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity < 1)
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be at least 1.");

            var catalogItem = await _catalogRepository.GetByIdAsync(line.CatalogItemId, cancellationToken);
            if (catalogItem is null)
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        var reference = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _settings.Currency, reference);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order.Id;
    }

    public async Task<Result<OrderPayment>> PayAsync(string buyerId, int orderId,
        PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        if (payment is null || payment.BuyerId != buyerId)
            return Result<OrderPayment>.NotFound();

        // Idempotent: a double-click on an already-authorized order returns the existing hold.
        if (payment.Status == PaymentStatus.Authorized)
            return Result<OrderPayment>.Success(payment);

        if (payment.Status != PaymentStatus.PendingAuthorization)
            return Result<OrderPayment>.Error($"Order is not awaiting payment (current payment status: {payment.Status}).");

        CardDetails? card = instruction.Card;
        string? vaultId = null;
        int? methodId = null;

        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(
                new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
            var pm = buyer?.FindPaymentMethod(savedId);
            if (pm is null || string.IsNullOrEmpty(pm.CardId))
                return Result<OrderPayment>.NotFound(); // not the buyer's card, or removed
            vaultId = pm.CardId;
            methodId = pm.Id;
            card = null;
        }
        else if (card is null)
        {
            return Result<OrderPayment>.Invalid(new List<ValidationError>
            {
                new()
                {
                    Identifier = "paymentSource",
                    ErrorMessage = "Provide either card details or a saved payment method id."
                }
            });
        }

        var authRequest = new AuthorizeRequest(
            payment.Amount,
            payment.CurrencyCode,
            payment.MerchantReference,
            buyerId,
            $"{payment.AuthorizationRequestId}:create",
            $"{payment.AuthorizationRequestId}:authorize",
            card,
            vaultId);

        var auth = await _paymentGateway.AuthorizeAsync(authRequest, cancellationToken);
        payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.AuthorizationStatus, auth.ExpiresAt, methodId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Result<OrderPayment>.Success(payment);
    }

    public async Task<Result<OrderPayment>> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        if (payment is null)
            return Result<OrderPayment>.NotFound();

        // Idempotent: already captured (or beyond) means fulfilment already happened.
        if (payment.IsCaptured)
            return Result<OrderPayment>.Success(payment);

        if (payment.Status != PaymentStatus.Authorized)
            return Result<OrderPayment>.Error($"Order is not authorized and cannot be fulfilled (current payment status: {payment.Status}).");

        // Renew a stale hold rather than failing the fulfilment outright.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.Now)
        {
            var reauth = await _paymentGateway.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                $"{payment.CaptureRequestId}:reauth", cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.AuthorizationStatus, reauth.ExpiresAt);
        }

        var capture = await _paymentGateway.CaptureAsync(
            payment.AuthorizationId!, payment.CaptureRequestId, cancellationToken);
        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Result<OrderPayment>.Success(payment);
    }

    public async Task<Result<OrderPayment>> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        if (payment is null)
            return Result<OrderPayment>.NotFound();

        // Idempotent: already voided.
        if (payment.Status == PaymentStatus.Voided)
            return Result<OrderPayment>.Success(payment);

        if (payment.Status != PaymentStatus.Authorized)
            return Result<OrderPayment>.Error($"Order is not authorized and cannot be cancelled (current payment status: {payment.Status}).");

        await _paymentGateway.VoidAsync(payment.AuthorizationId!, payment.VoidRequestId, cancellationToken);
        payment.MarkVoided("VOIDED");
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Result<OrderPayment>.Success(payment);
    }

    public async Task<Result<PaymentRefund>> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        if (payment is null || payment.BuyerId != buyerId)
            return Result<PaymentRefund>.NotFound();

        // Idempotent: the same key never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return Result<PaymentRefund>.Success(existing);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            return Result<PaymentRefund>.Error($"Order has not been captured and cannot be refunded (current payment status: {payment.Status}).");

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
            return Result<PaymentRefund>.Error("There is no remaining captured amount to refund.");

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            return Result<PaymentRefund>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "amount", ErrorMessage = "Refund amount must be greater than zero." }
            });
        if (refundAmount > remaining)
            return Result<PaymentRefund>.Invalid(new List<ValidationError>
            {
                new() { Identifier = "amount", ErrorMessage = "Refund amount exceeds the remaining refundable balance." }
            });

        var result = await _paymentGateway.RefundAsync(
            payment.CaptureId!, refundAmount, payment.CurrencyCode, idempotencyKey, cancellationToken);
        var refund = payment.AddRefund(result.RefundId, refundAmount, result.Status ?? "PENDING", idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Result<PaymentRefund>.Success(refund);
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByBuyerSpecification(buyerId), cancellationToken);

        var byOrder = payments.ToDictionary(p => p.OrderId);
        return orders
            .Select(o => new OrderWithPayment(o, byOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(cancellationToken);

        var paymentsByReference = payments
            .Where(p => !string.IsNullOrEmpty(p.MerchantReference))
            .GroupBy(p => p.MerchantReference)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var inPayPalNotInEShop = new List<ReconciliationTransaction>();
        var matchedReferences = new HashSet<string>();

        foreach (var transaction in transactions)
        {
            if (transaction.InvoiceId is not null &&
                paymentsByReference.TryGetValue(transaction.InvoiceId, out var payment))
            {
                matched.Add(new ReconciliationMatch(
                    payment.OrderId, payment.MerchantReference, payment.Amount,
                    payment.Status.ToString(), transaction));
                matchedReferences.Add(payment.MerchantReference);
            }
            else
            {
                inPayPalNotInEShop.Add(transaction);
            }
        }

        var inEShopNotInPayPal = payments
            .Where(p => p.CapturedDate is { } captured && captured >= from && captured <= to
                        && !matchedReferences.Contains(p.MerchantReference))
            .Select(p => new ReconciliationEShopEntry(
                p.OrderId, p.MerchantReference, p.CapturedGross ?? p.Amount,
                p.Status.ToString(), p.CapturedDate))
            .ToList();

        return new ReconciliationReport(from, to, matched, inPayPalNotInEShop, inEShopNotInPayPal);
    }
}
