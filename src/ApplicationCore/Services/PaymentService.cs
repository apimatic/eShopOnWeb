using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the payment lifecycle around an order. Each action is separately invocable and
/// idempotent in effect: a per-order in-process lock plus persisted PayPal-owned state ensure a
/// double-click never authorizes or captures the shopper twice.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentConfiguration _config;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    // In-process, per-order serialization. The app runs as a single host with an in-memory store,
    // so this is sufficient to make double-clicks idempotent in effect.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IPaymentConfiguration config,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _config = config;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    private static SemaphoreSlim LockFor(int orderId) => _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));

    public async Task<Result<int>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
            return Result<int>.Invalid(ServiceResults.Validation("items", "At least one order line is required."));

        if (lines.Any(l => l.Quantity <= 0))
            return Result<int>.Invalid(ServiceResults.Validation("quantity", "Every quantity must be greater than zero."));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            return Result<int>.Invalid(ServiceResults.Validation("items", $"Unknown catalog item id(s): {string.Join(", ", missing)}."));

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = _uriComposer.ComposePicUri(string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Order {0} placed by {1} awaiting payment ({2} line(s), total {3}).", order.Id, buyerId, orderItems.Count, order.Total());
        return Result<int>.Success(order.Id);
    }

    public async Task<Result<Payment>> PayOrderAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct = default)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
            if (order is null || order.BuyerId != buyerId)
                return Result<Payment>.NotFound();

            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

            // Idempotent re-entry.
            if (payment is not null)
            {
                switch (payment.Status)
                {
                    case PaymentStatus.Authorized:
                        return Result<Payment>.Success(payment); // double-click: hold already placed
                    case PaymentStatus.Captured:
                    case PaymentStatus.PartiallyRefunded:
                    case PaymentStatus.Refunded:
                        return Result<Payment>.Invalid(ServiceResults.Conflict("Order has already been captured."));
                    case PaymentStatus.Voided:
                        return Result<Payment>.Invalid(ServiceResults.Conflict("Order has been cancelled and can no longer be paid."));
                    // PaymentStatus.Failed / Pending: fall through and retry the authorization.
                }
            }

            if (order.Status != OrderStatus.AwaitingPayment && payment is null)
                return Result<Payment>.Invalid(ServiceResults.Conflict($"Order is not awaiting payment (status: {order.Status})."));

            // Resolve the instrument: raw card OR one of the caller's saved cards.
            var authRequest = new GatewayAuthorizeRequest
            {
                Amount = order.Total(),
                CurrencyCode = _config.CurrencyCode,
                InvoiceId = OrderCorrelation.UniqueForOrder(orderId)
            };

            if (instruction.SavedPaymentMethodId is int savedId)
            {
                var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdForBuyerSpec(savedId, buyerId), ct);
                if (saved is null)
                    return Result<Payment>.Invalid(ServiceResults.Validation("savedPaymentMethodId", "Saved card not found."));
                authRequest.VaultId = saved.VaultId;
            }
            else if (instruction.Card is not null)
            {
                authRequest.Card = instruction.Card;
            }
            else
            {
                return Result<Payment>.Invalid(ServiceResults.Validation("paymentSource", "Provide either card details or a savedPaymentMethodId."));
            }

            payment ??= new Payment(orderId, buyerId, _config.CurrencyCode, order.Total());
            if (payment.Id == 0)
                await _paymentRepository.AddAsync(payment, ct);

            var idempotencyKey = $"authorize-{orderId}-{Guid.NewGuid():N}";
            try
            {
                var result = await _gateway.AuthorizeAsync(authRequest, idempotencyKey, ct);
                payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus, result.ExpiresAt, result.CardBrand, result.CardLast4);
                await _paymentRepository.UpdateAsync(payment, ct);

                order.SetPaymentAuthorized();
                await _orderRepository.UpdateAsync(order, ct);

                _logger.LogInformation("Order {0} authorized: paypalOrder={1} auth={2} status={3}.", orderId, result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus);
                return Result<Payment>.Success(payment);
            }
            catch (PaymentGatewayException ex)
            {
                payment.MarkFailed();
                await _paymentRepository.UpdateAsync(payment, ct);
                _logger.LogWarning("Order {0} authorization failed: {1}", orderId, ex.Message);
                return Result<Payment>.Error(ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<Payment>> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId, ct);
            if (order is null)
                return Result<Payment>.NotFound();

            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
            if (payment is null)
                return Result<Payment>.Invalid(ServiceResults.Validation("payment", "Order has no authorized payment to capture."));

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                return Result<Payment>.Success(payment); // already fulfilled — idempotent

            if (payment.Status != PaymentStatus.Authorized)
                return Result<Payment>.Invalid(ServiceResults.Conflict($"Order cannot be fulfilled in payment state {payment.Status}."));

            GatewayCaptureResult capture;
            try
            {
                capture = await CaptureRenewingIfStaleAsync(payment, ct);
            }
            catch (AuthorizationUnrenewableException ex)
            {
                _logger.LogWarning("Order {0} fulfilment blocked: {1}", orderId, ex.Message);
                return Result<Payment>.Error(ex.Message);
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning("Order {0} capture failed: {1}", orderId, ex.Message);
                return Result<Payment>.Error(ex.Message);
            }

            payment.SetCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, ct);

            order.SetFulfilled();
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Order {0} fulfilled: capture={1} gross={2} fee={3} net={4}.", orderId, capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            return Result<Payment>.Success(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Capture the payment, renewing (reauthorizing) a stale hold first. If the hold has expired
    /// and cannot be renewed, throws <see cref="AuthorizationUnrenewableException"/> with an
    /// operator-actionable message.
    /// </summary>
    private async Task<GatewayCaptureResult> CaptureRenewingIfStaleAsync(Payment payment, CancellationToken ct)
    {
        var authId = payment.AuthorizationId!;
        var state = await _gateway.GetAuthorizationAsync(authId, ct);

        var expired = !string.Equals(state.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
                      || (state.ExpiresAt.HasValue && state.ExpiresAt.Value <= DateTimeOffset.UtcNow);

        if (string.Equals(state.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationUnrenewableException(payment.OrderId, authId,
                $"The payment authorization for order {payment.OrderId} is {state.Status} and cannot be captured. Ask the shopper to authorize payment again (POST /api/orders/{payment.OrderId}/pay) before fulfilling.");
        }

        if (expired)
        {
            authId = await RenewAuthorizationAsync(payment, ct);
        }

        try
        {
            return await _gateway.CaptureAsync(authId, $"capture-{authId}", ct);
        }
        catch (PaymentGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold went stale between our check and the capture; renew once and retry.
            authId = await RenewAuthorizationAsync(payment, ct);
            return await _gateway.CaptureAsync(authId, $"capture-{authId}", ct);
        }
    }

    private async Task<string> RenewAuthorizationAsync(Payment payment, CancellationToken ct)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, $"reauth-{payment.OrderId}-{Guid.NewGuid():N}", ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {0} authorization renewed: new auth={1}.", payment.OrderId, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationUnrenewableException(payment.OrderId, payment.AuthorizationId!,
                $"The payment authorization for order {payment.OrderId} has expired and can no longer be renewed ({ex.Message}). Ask the shopper to authorize payment again (POST /api/orders/{payment.OrderId}/pay) before fulfilling.", ex);
        }
    }

    private static bool IsExpiredAuthorization(PaymentGatewayException ex)
    {
        var issue = ex.ProcessorIssue ?? string.Empty;
        return issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || issue.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) && issue.Contains("STATUS", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Result<Payment>> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId, ct);
            if (order is null)
                return Result<Payment>.NotFound();

            if (order.Status == OrderStatus.Fulfilled)
                return Result<Payment>.Invalid(ServiceResults.Conflict("Order has already been fulfilled; refund it instead of cancelling."));

            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

            if (order.Status == OrderStatus.Cancelled)
                return Result<Payment>.Success(payment!); // idempotent (payment may be null)

            if (payment is not null && payment.Status == PaymentStatus.Captured)
                return Result<Payment>.Invalid(ServiceResults.Conflict("Payment has been captured; refund it instead of cancelling."));

            if (payment is not null && payment.Status == PaymentStatus.Authorized)
            {
                try
                {
                    await _gateway.VoidAsync(payment.AuthorizationId!, $"void-{payment.AuthorizationId}", ct);
                }
                catch (PaymentGatewayException ex)
                {
                    return Result<Payment>.Error($"Could not release the held funds for order {orderId}: {ex.Message}");
                }
                payment.SetVoided();
                await _paymentRepository.UpdateAsync(payment, ct);
            }

            order.SetCancelled();
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Order {0} cancelled; any held funds released.", orderId);
            return Result<Payment>.Success(payment!);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<PaymentRefund>> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result<PaymentRefund>.Invalid(ServiceResults.Validation("idempotencyKey", "An idempotency key is required for refunds."));

        var gate = LockFor(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId, ct);
            if (order is null || order.BuyerId != buyerId)
                return Result<PaymentRefund>.NotFound();

            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
            if (payment is null || payment.CaptureId is null)
                return Result<PaymentRefund>.Invalid(ServiceResults.Validation("payment", "Order has no captured payment to refund; refunds are only possible after fulfilment."));

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
                return Result<PaymentRefund>.Invalid(ServiceResults.Conflict($"Order cannot be refunded in payment state {payment.Status}."));

            // Idempotent replay under the same key.
            var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
                return Result<PaymentRefund>.Success(existing);

            var remaining = payment.RefundableRemaining();
            var refundAmount = amount ?? remaining;

            if (refundAmount <= 0m)
                return Result<PaymentRefund>.Invalid(ServiceResults.Validation("amount", "Refund amount must be greater than zero."));

            if (refundAmount > remaining)
                return Result<PaymentRefund>.Invalid(ServiceResults.Validation("amount", $"Refund amount {refundAmount} exceeds the refundable remaining {remaining}."));

            GatewayRefundResult result;
            try
            {
                var refundInvoiceId = OrderCorrelation.UniqueForOrder(orderId, "R");
                // Derive the PayPal-Request-Id from the (globally-unique) capture id plus the caller's
                // key, so a replay under the same key maps to the same PayPal request, while the raw key
                // stays generic across accounts/runs. DB dedup above already short-circuits true replays.
                var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
                result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.CurrencyCode, refundInvoiceId, payPalRequestId, ct);
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning("Order {0} refund failed: {1}", orderId, ex.Message);
                return Result<PaymentRefund>.Error(ex.Message);
            }

            var refund = new PaymentRefund(result.RefundId, idempotencyKey, result.Amount, result.CurrencyCode, result.Status);
            payment.AddRefund(refund);
            await _paymentRepository.UpdateAsync(payment, ct);

            order.SetRefunded(partial: payment.RefundableRemaining() > 0m);
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Order {0} refunded {1}: refund={2} status={3} remaining={4}.", orderId, result.Amount, result.RefundId, result.Status, payment.RefundableRemaining());
            return Result<PaymentRefund>.Success(refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<IReadOnlyList<OrderWithPayment>>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = orderIds.Count == 0
            ? new List<Payment>()
            : (await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpec(orderIds), ct)).ToList();
        var byOrder = payments.ToDictionary(p => p.OrderId);

        IReadOnlyList<OrderWithPayment> view = orders
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();

        return Result<IReadOnlyList<OrderWithPayment>>.Success(view);
    }
}
