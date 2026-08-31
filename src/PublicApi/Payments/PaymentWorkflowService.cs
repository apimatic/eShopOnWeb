using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowService
{
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalOptions _options;
    private readonly KeyedOperationLock _operationLock;

    public PaymentWorkflowService(
        CatalogContext db,
        IPayPalGateway payPal,
        PayPalOptions options,
        KeyedOperationLock operationLock)
    {
        _db = db;
        _payPal = payPal;
        _options = options;
        _operationLock = operationLock;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(
        string buyerId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        if (request.Items is null || request.Items.Count == 0)
            throw BadRequest("At least one catalog item is required.");
        if (request.ShippingAddress is null)
            throw BadRequest("A shipping address is required.");

        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(g => new OrderLineInput(g.Key, g.Sum(x => x.Quantity)))
            .ToList();
        if (requestedItems.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw BadRequest("Catalog item identifiers and quantities must be positive.");

        var ids = requestedItems.Select(x => x.CatalogItemId).ToArray();
        var catalog = await _db.CatalogItems
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (catalog.Count != ids.Length)
            throw BadRequest("One or more catalog items do not exist.");

        var orderItems = requestedItems.Select(line =>
        {
            var item = catalog[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price,
                line.Quantity);
        }).ToList();

        var shipping = request.ShippingAddress;
        if (new[] { shipping.Street, shipping.City, shipping.Country, shipping.ZipCode }.Any(string.IsNullOrWhiteSpace))
            throw BadRequest("The shipping address is incomplete.");

        var order = new Order(
            buyerId,
            new ApplicationCore.Entities.OrderAggregate.Address(
                shipping.Street,
                shipping.City,
                shipping.State ?? string.Empty,
                shipping.Country,
                shipping.ZipCode),
            orderItems,
            _options.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new PlaceOrderResponse(order.Id, order.OrderStatus.ToString(), order.Total(), _options.Currency);
    }

    public async Task<OrderResponse> PayAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePaymentChoice(request);
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        using var paymentMethodOperation = request.PaymentMethodId is int methodId
            ? await _operationLock.AcquireAsync($"payment-method:{methodId}", cancellationToken)
            : null;
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.PaymentStatus == PaymentLifecycleStatus.Authorized)
            return ToResponse(order);
        if (order.PaymentStatus is not (PaymentLifecycleStatus.AwaitingPayment or PaymentLifecycleStatus.Authorizing or PaymentLifecycleStatus.Failed))
            throw Conflict($"Order cannot be authorized while payment is {order.PaymentStatus}.");

        string? vaultId = null;
        if (request.PaymentMethodId is int paymentMethodId)
        {
            var paymentMethod = await _db.PaymentMethods.SingleOrDefaultAsync(
                x => x.Id == paymentMethodId && x.BuyerId == buyerId && x.IsActive,
                cancellationToken);
            if (paymentMethod is null) throw NotFound("Saved payment method was not found.");
            vaultId = paymentMethod.PayPalPaymentTokenId;
        }

        order.EnsurePaymentRequestIds();
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await _payPal.AuthorizeAsync(
                order.Id,
                order.Total(),
                request.Card,
                vaultId,
                order.CreateOrderRequestId!,
                order.AuthorizeRequestId!,
                cancellationToken);
            order.RecordPayPalOrder(result.OrderId, result.OrderStatus);
            order.RecordAuthorization(
                result.AuthorizationId,
                result.AuthorizationStatus,
                result.CreatedAt,
                result.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            return ToResponse(order);
        }
        catch (PaymentApiException)
        {
            order.RecordPaymentFailure();
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.OrderStatus is OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded or OrderLifecycleStatus.Refunded)
            return ToResponse(order);
        if (order.OrderStatus == OrderLifecycleStatus.Cancelled)
            throw Conflict("A cancelled order cannot be fulfilled.");
        if (string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
            throw Conflict("The order has no PayPal authorization. The shopper must pay before fulfilment.");

        if (order.PaymentStatus == PaymentLifecycleStatus.CapturePending && !string.IsNullOrWhiteSpace(order.PayPalCaptureId))
        {
            var refreshed = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
            ApplyCapture(order, refreshed);
            await _db.SaveChangesAsync(cancellationToken);
            return ToResponse(order);
        }

        var authorizationCreated = order.AuthorizationCreatedAt ?? order.OrderDate;
        var age = DateTimeOffset.UtcNow - authorizationCreated;
        if (age >= TimeSpan.FromDays(30))
            throw Conflict(
                $"Authorization {order.PayPalAuthorizationId} is older than 29 days and cannot be renewed. The shopper must re-pay.");

        var renewed = false;
        if (age >= TimeSpan.FromDays(3))
        {
            await RenewAuthorizationAsync(order, cancellationToken);
            renewed = true;
        }

        order.EnsureCaptureRequestId();
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            var capture = await _payPal.CaptureAsync(
                order.PayPalAuthorizationId!,
                order.Total(),
                order.CaptureRequestId!,
                cancellationToken);
            ApplyCapture(order, capture);
            await _db.SaveChangesAsync(cancellationToken);
            return ToResponse(order);
        }
        catch (PaymentApiException ex) when (!renewed && age >= TimeSpan.FromDays(3) && age < TimeSpan.FromDays(30) && (int)ex.StatusCode is 409 or 422)
        {
            await RenewAuthorizationAsync(order, cancellationToken);
            var capture = await _payPal.CaptureAsync(
                order.PayPalAuthorizationId!,
                order.Total(),
                order.CaptureRequestId!,
                cancellationToken);
            ApplyCapture(order, capture);
            await _db.SaveChangesAsync(cancellationToken);
            return ToResponse(order);
        }
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.OrderStatus == OrderLifecycleStatus.Cancelled) return ToResponse(order);
        if (order.PayPalCaptureId is not null || order.PaymentStatus is PaymentLifecycleStatus.Captured or PaymentLifecycleStatus.CapturePending or PaymentLifecycleStatus.PartiallyRefunded or PaymentLifecycleStatus.Refunded)
            throw Conflict("Captured funds cannot be cancelled; create a refund instead.");
        if (string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
            throw Conflict("The order has no authorization to release.");

        order.EnsureVoidRequestId();
        await _db.SaveChangesAsync(cancellationToken);
        var status = await _payPal.VoidAsync(order.PayPalAuthorizationId, order.VoidRequestId!, cancellationToken);
        if (!string.Equals(status, "VOIDED", StringComparison.Ordinal))
            throw Conflict($"PayPal authorization {order.PayPalAuthorizationId} is {status}; it was not released.");
        order.RecordVoided(status);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(order);
    }

    public async Task<RefundResponse> RefundAsync(
        string buyerId,
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw BadRequest("IdempotencyKey is required and cannot exceed 128 characters.");

        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        await using var reservationTransaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (string.IsNullOrWhiteSpace(order.PayPalCaptureId) || order.CapturedAmount is null)
            throw Conflict("Only a captured payment can be refunded.");

        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null && existing.Status != "RESERVED")
        {
            if (reservationTransaction is not null) await reservationTransaction.CommitAsync(cancellationToken);
            if (existing.Status == "PENDING" && existing.PayPalRefundId is not null)
            {
                var refreshed = await _payPal.GetRefundAsync(existing.PayPalRefundId, existing.Amount, cancellationToken);
                existing.RecordProviderResult(refreshed.RefundId, refreshed.Status, refreshed.Amount, refreshed.UpdatedAt);
                order.RecalculateRefundState();
                await _db.SaveChangesAsync(cancellationToken);
            }
            return ToResponse(existing);
        }

        var available = order.CapturedAmount.Value - order.ReservedRefundAmount();
        var amount = existing?.Amount ?? request.Amount ?? available;
        if (amount <= 0) throw BadRequest("Refund amount must be greater than zero.");
        if (existing is null && amount > available)
            throw Conflict($"Refund exceeds the remaining refundable amount of {available:0.00} {order.Currency}.");
        if (existing is not null && request.Amount is not null && request.Amount != existing.Amount)
            throw Conflict("This idempotency key is already reserved for a different refund amount.");

        var refund = existing ?? order.ReserveRefund(request.IdempotencyKey, Guid.NewGuid().ToString("N"), amount);
        if (existing is null) await _db.SaveChangesAsync(cancellationToken);
        if (reservationTransaction is not null) await reservationTransaction.CommitAsync(cancellationToken);

        var fullRemainder = request.Amount is null || amount == available;
        try
        {
            var provider = await _payPal.RefundAsync(
                order.PayPalCaptureId,
                amount,
                fullRemainder,
                refund.ProviderRequestId,
                cancellationToken);
            if (!string.Equals(provider.Currency, order.Currency, StringComparison.Ordinal) || provider.Amount != amount)
                throw new PaymentApiException("PayPal returned a refund for the wrong amount or currency.", HttpStatusCode.BadGateway);
            refund.RecordProviderResult(provider.RefundId, provider.Status, provider.Amount, provider.UpdatedAt);
            order.RecalculateRefundState();
            await _db.SaveChangesAsync(cancellationToken);
            return ToResponse(refund);
        }
        catch (PaymentApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            refund.Release("FAILED");
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderResponse>> MyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToResponse).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        ValidateCard(request.Card);
        using var operation = await _operationLock.AcquireAsync($"payment-methods:{buyerId}", cancellationToken);
        var provider = await _payPal.SaveCardAsync(
            buyerId,
            request.Card,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            cancellationToken);
        var paymentMethod = new PaymentMethod(
            buyerId,
            provider.PaymentTokenId,
            provider.CustomerId,
            provider.Brand,
            provider.CardType,
            provider.LastDigits,
            provider.Expiry);
        _db.PaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(paymentMethod);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        return await _db.PaymentMethods
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.CardType, x.LastDigits, x.Expiry))
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"payment-method:{paymentMethodId}", cancellationToken);
        var paymentMethod = await _db.PaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId,
            cancellationToken);
        if (paymentMethod is null) throw NotFound("Saved payment method was not found.");
        if (!paymentMethod.IsActive) return;
        await _payPal.DeleteCardAsync(paymentMethod.PayPalPaymentTokenId, cancellationToken);
        paymentMethod.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to) throw BadRequest("The reconciliation 'from' instant must not be after 'to'.");
        var provider = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(x => x.Refunds)
            .Where(x =>
                (x.CapturedAt >= from && x.CapturedAt <= to) ||
                x.Refunds.Any(r => r.UpdatedAt >= from && r.UpdatedAt <= to))
            .ToListAsync(cancellationToken);

        var matchedOrders = new HashSet<int>();
        var rows = new List<ProviderTransactionResponse>();
        foreach (var transaction in provider.Transactions)
        {
            var order = MatchOrder(transaction, orders);
            if (order is not null) matchedOrders.Add(order.Id);
            rows.Add(new ProviderTransactionResponse(
                transaction.TransactionId,
                transaction.PayPalReferenceId,
                transaction.EventCode,
                transaction.InitiatedAt,
                transaction.Status,
                transaction.Amount,
                transaction.Currency,
                transaction.Fee,
                transaction.InvoiceId,
                transaction.CustomId,
                order?.Id,
                order is null ? "PayPalOnly" : "Matched"));
        }

        var eShopOnly = orders.Select(x => x.Id).Where(id => !matchedOrders.Contains(id)).Distinct().OrderBy(x => x).ToList();
        return new ReconciliationResponse(from, to, provider.LastRefreshedAt, rows, eShopOnly);
    }

    private async Task RenewAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        order.EnsureReauthorizeRequestId();
        await _db.SaveChangesAsync(cancellationToken);
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                order.Total(),
                order.ReauthorizeRequestId!,
                cancellationToken);
            order.RecordAuthorization(renewed.Id, renewed.Status, renewed.CreatedAt, renewed.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (PaymentApiException ex)
        {
            throw new PaymentApiException(
                $"Authorization {order.PayPalAuthorizationId} could not be renewed. The shopper must re-pay.",
                HttpStatusCode.Conflict,
                ex.ProviderDebugId,
                innerException: ex);
        }
    }

    private static void ApplyCapture(Order order, CaptureResult capture)
    {
        if (capture.Gross != order.Total() || !string.Equals(capture.Currency, order.Currency, StringComparison.Ordinal))
            throw new PaymentApiException("PayPal returned a capture for the wrong amount or currency.", HttpStatusCode.BadGateway);
        if (string.Equals(capture.Status, "COMPLETED", StringComparison.Ordinal))
        {
            order.RecordCapture(capture.CaptureId, capture.Status, capture.Gross, capture.Fee, capture.Net, capture.CreatedAt);
            return;
        }
        if (string.Equals(capture.Status, "PENDING", StringComparison.Ordinal))
        {
            order.RecordCapturePending(capture.CaptureId, capture.Status);
            return;
        }
        throw Conflict($"PayPal capture {capture.CaptureId} is {capture.Status}; the order was not fulfilled.");
    }

    private async Task<Order> OwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        var order = await _db.Orders
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        return order ?? throw NotFound("Order was not found.");
    }

    private async Task<Order> OrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw NotFound("Order was not found.");
    }

    private static Order? MatchOrder(ProviderTransaction transaction, IReadOnlyList<Order> orders)
    {
        if (int.TryParse(transaction.InvoiceId ?? transaction.CustomId, NumberStyles.None, CultureInfo.InvariantCulture, out var orderId))
        {
            var direct = orders.SingleOrDefault(x => x.Id == orderId);
            if (direct is not null) return direct;
        }
        return orders.SingleOrDefault(order =>
            transaction.TransactionId == order.PayPalCaptureId ||
            transaction.PayPalReferenceId == order.PayPalCaptureId ||
            transaction.PayPalReferenceId == order.PayPalAuthorizationId ||
            transaction.PayPalReferenceId == order.PayPalOrderId ||
            order.Refunds.Any(refund =>
                transaction.TransactionId == refund.PayPalRefundId ||
                transaction.PayPalReferenceId == refund.PayPalRefundId));
    }

    private static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.OrderDate,
        order.OrderStatus.ToString(),
        order.Total(),
        order.Currency,
        new PaymentStateResponse(
            order.PaymentStatus.ToString(),
            order.PayPalOrderId,
            order.PayPalAuthorizationId,
            order.PayPalAuthorizationStatus,
            order.PayPalCaptureId,
            order.PayPalCaptureStatus,
            order.CapturedAmount,
            order.PayPalFee,
            order.NetProceeds,
            order.RefundedAmount(),
            order.Refunds.Select(ToResponse).ToList()));

    private static RefundResponse ToResponse(PaymentRefund refund) =>
        new(refund.Id, refund.Status, refund.Amount, refund.Currency);

    private static PaymentMethodResponse ToResponse(PaymentMethod method) =>
        new(method.Id, method.Brand, method.CardType, method.LastDigits, method.Expiry);

    private static void ValidatePaymentChoice(PayOrderRequest request)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw BadRequest("Provide exactly one of card or paymentMethodId.");
        if (request.Card is not null) ValidateCard(request.Card);
        if (request.PaymentMethodId <= 0) throw BadRequest("paymentMethodId must be positive.");
    }

    private static void ValidateCard(CardInput card)
    {
        if (new[] { card.Name, card.Number, card.Expiry, card.SecurityCode }.Any(string.IsNullOrWhiteSpace))
            throw BadRequest("Card name, number, expiry, and security code are required.");
        if (card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw BadRequest("Card billing address and countryCode are required.");
    }

    private static PaymentApiException BadRequest(string message) => new(message, HttpStatusCode.BadRequest);
    private static PaymentApiException Conflict(string message) => new(message, HttpStatusCode.Conflict);
    private static PaymentApiException NotFound(string message) => new(message, HttpStatusCode.NotFound);
    private static PaymentApiException Unauthorized() => new("Authentication is required.", HttpStatusCode.Unauthorized);
}
