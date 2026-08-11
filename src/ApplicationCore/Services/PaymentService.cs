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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IPaymentSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.CurrencyCode;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }

        // Merge duplicate lines and validate quantities.
        var quantities = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            quantities[line.CatalogItemId] = quantities.GetValueOrDefault(line.CatalogItemId) + line.Quantity;
        }

        var ids = quantities.Keys.ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for {buyerId}, total {order.Total():0.00} {Currency}.");
        return order;
    }

    public async Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken cancellationToken = default)
    {
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent: a repeated authorize returns the existing hold rather than placing a second one.
        if (payment.IsAuthorized)
        {
            return payment;
        }
        if (payment.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            throw new PaymentException($"Order {orderId} has already been paid and cannot be authorized again.");
        }
        if (payment.Status is OrderPaymentStatus.Canceled)
        {
            throw new PaymentException($"Order {orderId} was cancelled and cannot be authorized.");
        }

        var (card, vaultId) = await ResolveInstrumentAsync(instruction, buyerId, cancellationToken);

        // Deterministic invoice id: stable across idempotent replays, unique per order.
        var invoiceId = $"ESHOP-{orderId}-{payment.AuthorizeRequestId[..8]}";
        var request = new AuthorizeRequest(
            payment.Amount,
            payment.CurrencyCode,
            payment.AuthorizeRequestId,
            invoiceId,
            orderId.ToString(),
            card,
            vaultId);

        AuthorizeResult result;
        try
        {
            result = await _payPal.CreateAndAuthorizeOrderAsync(request, cancellationToken);
        }
        catch (PayPalApiException)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        if (result.RequiresPayerAction)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PayPalChallengeException(
                $"PayPal requires the shopper to approve this card payment in a browser (order {orderId}). " +
                "This is not supported by the API-only flow; use a card that does not trigger a challenge (e.g. the sandbox test card).");
        }

        if (string.IsNullOrEmpty(result.AuthorizationId))
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException(
                $"PayPal did not create an authorization for order {orderId} (order status: {result.OrderStatus}).");
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId!, result.AuthorizationStatus ?? "CREATED",
            result.ExpiresAt, result.InstrumentDescription, invoiceId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId}.");
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentOrThrowAsync(orderId, cancellationToken);

        // Idempotent: already captured.
        if (payment.IsCaptured)
        {
            return payment;
        }
        if (payment.Status is not OrderPaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled because its payment is {payment.Status}, not Authorized.");
        }

        var authorizationId = payment.AuthorizationId!;

        // Read PayPal's authoritative view of the hold; renew it if it has gone stale before fulfilment.
        var auth = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        payment.RefreshAuthorizationStatus(auth.Status, auth.ExpiresAt);

        if (auth.Status is "VOIDED" or "DENIED")
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException(
                $"Order {orderId} cannot be fulfilled: its authorization is {auth.Status} and can no longer be captured. The shopper must pay again.");
        }

        if (payment.IsAuthorizationStale(DateTimeOffset.UtcNow))
        {
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, amount: null, payment.CurrencyCode,
                payment.CaptureRequestId, payment.InvoiceId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.Issues.Any(i => i.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)))
        {
            // Race: the hold expired between our check and the capture. Renew once and retry.
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, amount: null, payment.CurrencyCode,
                payment.CaptureRequestId, payment.InvoiceId, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Gross, capture.Fee, capture.Net);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.Gross:0.00} {capture.CurrencyCode} " +
            $"(fee {capture.Fee:0.00}, net {capture.Net:0.00}), capture {capture.CaptureId}.");
        return payment;
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                $"reauth-{payment.AuthorizationId}", cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Renewed stale authorization for order {payment.OrderId}: new authorization {reauth.AuthorizationId}.");
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            var detail = ex.Issues.Count > 0 ? string.Join("; ", ex.Issues) : ex.Message;
            throw new PaymentException(
                $"Order {payment.OrderId} cannot be fulfilled: the authorization has expired and could not be renewed ({detail}). The shopper must place and pay for a new order.", ex);
        }
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentOrThrowAsync(orderId, cancellationToken);

        if (payment.Status is OrderPaymentStatus.Canceled)
        {
            return payment; // idempotent
        }
        if (payment.Status is not (OrderPaymentStatus.Authorized or OrderPaymentStatus.AwaitingPayment))
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled because its payment is {payment.Status}. Cancellation is only possible before fulfilment; use a refund instead.");
        }

        // Release the held funds at PayPal (only if a hold was actually placed).
        if (payment.Status is OrderPaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, $"void-{payment.AuthorizationId}", cancellationToken);
        }

        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}: authorization released.");
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent: a repeated request under the same key returns the original refund.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded) || payment.CaptureId is null)
        {
            throw new PaymentException($"Order {orderId} cannot be refunded because its payment is {payment.Status}. A refund is only possible after fulfilment.");
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException($"Refund amount must be greater than zero for order {orderId}.");
        }
        if (refundAmount - remaining > 0.0001m)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} {payment.CurrencyCode} exceeds the refundable remaining amount of {remaining:0.00} {payment.CurrencyCode} for order {orderId}.");
        }

        var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.CurrencyCode,
            idempotencyKey, payment.InvoiceId, orderId.ToString(), cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Refunded {result.Amount:0.00} {result.CurrencyCode} on order {orderId}: refund {result.RefundId} ({payment.Status}).");
        return refund;
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new MyOrderView(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<OrderPayment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null || !string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<OrderPayment> GetPaymentOrThrowAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<(CardDetails? card, string? vaultId)> ResolveInstrumentAsync(PayInstruction instruction, string buyerId, CancellationToken cancellationToken)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedCardId.HasValue;

        if (hasCard == hasSaved)
        {
            throw new PaymentException("Provide exactly one of a card or a saved payment method id to pay.");
        }

        if (hasSaved)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForOwnerSpec(instruction.SavedCardId!.Value, buyerId), cancellationToken);
            if (savedCard is null)
            {
                throw new PaymentResourceNotFoundException($"Saved payment method {instruction.SavedCardId} was not found.");
            }
            return (null, savedCard.VaultId);
        }

        return (instruction.Card, null);
    }
}
