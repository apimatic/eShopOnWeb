using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Orchestrates place → pay → fulfil / cancel / refund on top of the existing order model and the PayPal
/// gateway. Idempotency is enforced by an application-owned claim row inserted before each provider write; a
/// deterministic PayPal request id is the money-layer backstop.
/// </summary>
public sealed class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<PaymentOperation> _operationRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<PaymentOperation> operationRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalOptions> options,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _operationRepository = operationRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(options.Value.Currency) ? "USD" : options.Value.Currency!.Trim().ToUpperInvariant();
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? shipping, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw PaymentException.Validation("An order must contain at least one line.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw PaymentException.Validation($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");

            var item = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw PaymentException.Validation($"Catalog item {line.CatalogItemId} does not exist.");

            var ordered = new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri));
            orderItems.Add(new OrderItem(ordered, item.Price, line.Quantity));
        }

        var address = shipping is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, ct);

        var total = order.Total();
        // Unique per placement so re-runs of the in-memory app (ids restart) never collide on PayPal's invoice uniqueness.
        var invoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new OrderPayment(order.Id, buyerId, _currency, total, invoiceId);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {OrderId} for {BuyerId} totalling {Total} {Currency}", order.Id, buyerId, total, _currency);
        return order.Id;
    }

    public async Task<OrderPaymentSummary> PayAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct)
    {
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.State == PaymentState.Authorized)
            return Summarize(payment); // already held — idempotent
        if (payment.State != PaymentState.AwaitingPayment)
            throw PaymentException.Conflict($"Order {orderId} is {payment.State} and cannot be paid.");

        var claim = await TryClaimAsync(payment.Id, PaymentOperation.Authorize, ct);
        if (claim is null)
            return Summarize(await GetOwnedPaymentAsync(orderId, buyerId, ct)); // concurrent double-submit

        try
        {
            var auth = await _gateway.AuthorizeOrderAsync(
                payment.InvoiceId, payment.Amount, payment.CurrencyCode, instrument, payment.InvoiceId, ct);
            payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Authorized order {OrderId}: paypalOrder={PayPalOrderId} auth={AuthId} status={Status}",
                orderId, auth.PayPalOrderId, auth.AuthorizationId, auth.Status);
            return Summarize(payment);
        }
        catch
        {
            // Release the claim so a retry is possible. The deterministic PayPal request id makes that retry an
            // idempotent replay, so an authorization created just before a failed read is not duplicated.
            await ReleaseClaimAsync(claim, ct);
            throw;
        }
    }

    public async Task<OrderPaymentSummary> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.State == PaymentState.Fulfilled)
            return Summarize(payment); // idempotent
        if (payment.State != PaymentState.Authorized || payment.AuthorizationId is null)
            throw PaymentException.Conflict($"Order {orderId} is {payment.State}; only an authorized order can be fulfilled.");

        var claim = await TryClaimAsync(payment.Id, PaymentOperation.Capture, ct);
        if (claim is null)
            return Summarize(await GetPaymentAsync(orderId, ct));

        // Renew a stale authorization before capturing, rather than failing the fulfilment outright.
        if (IsStale(payment.AuthorizationExpiresAt))
        {
            try
            {
                var renewal = await _gateway.ReauthorizeAsync(
                    payment.AuthorizationId, payment.Amount, payment.CurrencyCode, $"reauth-{payment.InvoiceId}", ct);
                payment.RenewAuthorization(renewal.AuthorizationId, renewal.Status, renewal.ExpiresAt);
                await _paymentRepository.UpdateAsync(payment, ct);
                _logger.LogInformation("Renewed authorization for order {OrderId}: auth={AuthId} expires={Expiry}",
                    orderId, renewal.AuthorizationId, renewal.ExpiresAt);
            }
            catch (PaymentException ex)
            {
                const string message = "The authorization has expired and can no longer be renewed. " +
                    "A new authorization is required: the order must be paid again before it can be fulfilled.";
                payment.SetError(message);
                await _paymentRepository.UpdateAsync(payment, ct);
                await ReleaseClaimAsync(claim, ct);
                _logger.LogWarning(ex, "Could not renew authorization for order {OrderId}", orderId);
                throw new PaymentException(409, message, ex);
            }
        }

        try
        {
            var capture = await _gateway.CaptureAsync(
                payment.AuthorizationId, payment.Amount, payment.CurrencyCode, payment.InvoiceId, $"cap-{payment.InvoiceId}", ct);
            payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Fulfilled order {OrderId}: capture={CaptureId} amount={Amount} fee={Fee} net={Net}",
                orderId, capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            return Summarize(payment);
        }
        catch
        {
            await ReleaseClaimAsync(claim, ct);
            throw;
        }
    }

    public async Task<OrderPaymentSummary> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.State == PaymentState.Cancelled)
            return Summarize(payment); // idempotent
        if (payment.State != PaymentState.Authorized || payment.AuthorizationId is null)
            throw PaymentException.Conflict($"Order {orderId} is {payment.State}; only an authorized order can be cancelled before fulfilment.");

        var claim = await TryClaimAsync(payment.Id, PaymentOperation.Void, ct);
        if (claim is null)
            return Summarize(await GetPaymentAsync(orderId, ct));

        try
        {
            await _gateway.VoidAsync(payment.AuthorizationId, $"void-{payment.InvoiceId}", ct);
            payment.MarkCancelled();
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Cancelled order {OrderId}: released authorization {AuthId}", orderId, payment.AuthorizationId);
            return Summarize(payment);
        }
        catch
        {
            await ReleaseClaimAsync(claim, ct);
            throw;
        }
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw PaymentException.Validation("A refund requires a caller-supplied idempotency key.");

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.State is not (PaymentState.Fulfilled or PaymentState.PartiallyRefunded))
            throw PaymentException.Conflict($"Order {orderId} is {payment.State}; only a fulfilled order can be refunded.");
        if (payment.CaptureId is null)
            throw PaymentException.Conflict($"Order {orderId} has no captured payment to refund.");

        // Same key already used → return that refund; never refund twice under one key.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
            return new RefundResponse(existing.PayPalRefundId ?? string.Empty, Summarize(payment));

        var refundable = payment.RefundableAmount();
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0m)
            throw PaymentException.Validation("There is nothing left to refund on this order.");
        if (refundAmount > refundable)
            throw PaymentException.Validation(
                $"Refund of {refundAmount:0.00} exceeds the {refundable:0.00} still refundable on this capture.");

        // Persist the claim (unique per order-payment + key) BEFORE calling PayPal.
        var refund = new PaymentRefund(idempotencyKey, refundAmount);
        payment.AddRefund(refund);
        try
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (DbUpdateException)
        {
            var reloaded = await GetOwnedPaymentAsync(orderId, buyerId, ct);
            var e = reloaded.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            return new RefundResponse(e?.PayPalRefundId ?? string.Empty, Summarize(reloaded));
        }

        try
        {
            var result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.CurrencyCode, idempotencyKey, ct);
            refund.MarkResult(result.RefundId, result.Status);
            payment.RecomputeRefundState();
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Refunded order {OrderId}: refund={RefundId} amount={Amount} status={Status}",
                orderId, result.RefundId, refundAmount, result.Status);
            return new RefundResponse(result.RefundId, Summarize(payment));
        }
        catch
        {
            // Keep the row marked failed so repeating the same key won't refund twice; a new key can retry.
            refund.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), ct);
        return payments.Select(Summarize).ToList();
    }

    // ----- helpers -----

    private async Task<OrderPayment> GetPaymentAsync(int orderId, CancellationToken ct) =>
        await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct)
        ?? throw PaymentException.NotFound($"Order {orderId} was not found.");

    private async Task<OrderPayment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);
        // A shopper must never see or act on another's order — hide existence rather than reveal it.
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw PaymentException.NotFound($"Order {orderId} was not found.");
        return payment;
    }

    private async Task<PaymentOperation?> TryClaimAsync(int orderPaymentId, string operationType, CancellationToken ct)
    {
        var claim = new PaymentOperation(orderPaymentId, operationType);
        try
        {
            await _operationRepository.AddAsync(claim, ct);
            return claim;
        }
        catch (DbUpdateException)
        {
            return null; // a concurrent request already claimed this operation
        }
    }

    private async Task ReleaseClaimAsync(PaymentOperation claim, CancellationToken ct)
    {
        try
        {
            await _operationRepository.DeleteAsync(claim, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release payment operation claim {ClaimId}", claim.Id);
        }
    }

    private static bool IsStale(DateTimeOffset? expiresAt) =>
        expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(1);

    private static OrderPaymentSummary Summarize(OrderPayment p) => new(
        p.OrderId,
        p.State.ToString(),
        p.Amount,
        p.CurrencyCode,
        p.InvoiceId,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.RefundedAmount(),
        p.RefundableAmount(),
        p.LastError,
        p.Refunds.Select(r => new RefundLine(r.Id, r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt)).ToList());
}
