using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationService(
    CatalogContext db,
    IPayPalGateway payPal,
    IOptions<PayPalSettings> options)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly string _currency = options.Value.Currency.ToUpperInvariant();

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw BadRequest("order_items_required", "At least one catalog item is required.");

        var requested = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineRequest(x.Key, x.Sum(y => y.Quantity)))
            .ToList();
        if (requested.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 1000))
            throw BadRequest("invalid_order_item", "Catalog item identifiers and quantities must be positive; quantity cannot exceed 1000.");

        var ids = requested.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count)
            throw new PaymentApiException((int)HttpStatusCode.NotFound, "catalog_item_not_found",
                "One or more catalog items do not exist.");

        var quantities = requested.ToDictionary(x => x.CatalogItemId, x => x.Quantity);
        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            quantities[item.Id])).ToList();

        var order = new Order(buyerId, items);
        order.InitializePayment(_currency);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        order.EnsurePaymentRequestIds();
        await db.SaveChangesAsync(cancellationToken);

        return new PlaceOrderResponse(order.Id, order.Total(), order.Currency, order.PaymentStatus.ToString());
    }

    public async Task<PaymentStateResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrder(orderId, buyerId, cancellationToken);
            if (order.PaymentStatus is PaymentStatus.Authorized or PaymentStatus.AuthorizationPending or
                PaymentStatus.Captured or PaymentStatus.CapturePending or PaymentStatus.PartiallyRefunded or
                PaymentStatus.Refunded)
                return Map(order);
            if (order.PaymentStatus == PaymentStatus.Cancelled)
                throw Conflict("order_cancelled", "A cancelled order cannot be paid.");
            if ((request.Card is null) == (request.PaymentMethodId is null))
                throw BadRequest("payment_source_required", "Supply either card details or one saved paymentMethodId, but not both.");

            string? vaultId = null;
            if (request.PaymentMethodId is not null)
            {
                var method = await db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                    x.Id == request.PaymentMethodId && x.BuyerId == buyerId && x.IsActive, cancellationToken);
                if (method?.PayPalVaultId is null)
                    throw new PaymentApiException((int)HttpStatusCode.NotFound, "payment_method_not_found",
                        "The saved payment method does not exist or is no longer active.");
                vaultId = method.PayPalVaultId;
            }

            order.InitializePayment(_currency);
            order.EnsurePaymentRequestIds();
            await db.SaveChangesAsync(cancellationToken);
            var result = await payPal.AuthorizeAsync(
                order.PaymentReference,
                order.Total(),
                order.Currency,
                order.CreatePaymentRequestId,
                order.AuthorizePaymentRequestId,
                request.Card is null ? null : Map(request.Card),
                vaultId,
                cancellationToken);

            order.RecordPayPalOrder(result.PayPalOrderId, result.OrderStatus);
            if (result.PayerActionRequired)
            {
                order.MarkPayerActionRequired(result.OrderStatus);
                await db.SaveChangesAsync(cancellationToken);
                throw Conflict("payer_action_required",
                    "PayPal requires browser approval for this card. The headless payment flow was stopped; use a different sandbox card or account configuration.");
            }

            if (result.AuthorizationId is null)
                throw new PayPalProviderException("PayPal did not return an authorization identifier.");
            order.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, result.CreatedAt, result.ExpiresAt);
            await db.SaveChangesAsync(cancellationToken);
            return Map(order);
        }
        catch (PayPalProviderException ex)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "paypal_error",
                ProviderMessage(ex), ex);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<PaymentStateResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await AnyOrder(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled)
                throw Conflict("order_cancelled", "A cancelled order cannot be fulfilled.");

            if (order.PayPalCaptureId is not null)
            {
                if (order.PayPalCaptureStatus != "COMPLETED")
                {
                    var refreshed = await payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
                    order.RecordCapture(refreshed.Id, refreshed.Status, refreshed.Amount, refreshed.Fee,
                        refreshed.Net, refreshed.CreatedAt);
                    await db.SaveChangesAsync(cancellationToken);
                }
                return Map(order);
            }

            if (order.PayPalAuthorizationId is null)
                throw Conflict("order_not_authorized", "The order must be paid and authorized before fulfilment.");

            var authorization = await payPal.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
            order.RecordAuthorization(order.PayPalAuthorizationId, authorization.AuthorizationStatus,
                authorization.CreatedAt, authorization.ExpiresAt);
            if (authorization.AuthorizationStatus is "VOIDED" or "DENIED")
            {
                await db.SaveChangesAsync(cancellationToken);
                throw Conflict("authorization_unavailable",
                    $"PayPal reports the authorization as {authorization.AuthorizationStatus}; collect payment again before fulfilment.");
            }
            if (authorization.AuthorizationStatus != "CREATED")
            {
                await db.SaveChangesAsync(cancellationToken);
                throw Conflict("authorization_not_ready",
                    $"PayPal reports the authorization as {authorization.AuthorizationStatus ?? "UNKNOWN"}; retry fulfilment after it becomes CREATED.");
            }

            var createdAt = authorization.CreatedAt ?? order.PayPalAuthorizationCreatedAt;
            if (createdAt is not null && createdAt <= DateTimeOffset.UtcNow.AddDays(-3))
            {
                try
                {
                    var reauthorizeRequestId = order.StartOrResumeReauthorization();
                    await db.SaveChangesAsync(cancellationToken);
                    var renewed = await payPal.ReauthorizeAsync(order.PayPalAuthorizationId, order.Total(),
                        order.Currency, reauthorizeRequestId, cancellationToken);
                    if (renewed.AuthorizationId is null)
                        throw new PayPalProviderException("PayPal did not return a renewed authorization identifier.");
                    order.RecordAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus,
                        renewed.CreatedAt, renewed.ExpiresAt);
                    order.CompleteReauthorization();
                    await db.SaveChangesAsync(cancellationToken);
                    if (renewed.AuthorizationStatus != "CREATED")
                        throw Conflict("authorization_cannot_be_renewed",
                            $"PayPal renewed the authorization with status {renewed.AuthorizationStatus ?? "UNKNOWN"}. Collect payment again before fulfilment.");
                }
                catch (PayPalProviderException ex)
                {
                    throw Conflict("authorization_cannot_be_renewed",
                        $"The PayPal authorization is stale and could not be renewed. {ProviderMessage(ex)} Collect payment again before fulfilment.");
                }
            }

            var capture = await payPal.CaptureAsync(order.PayPalAuthorizationId!, order.Total(), order.Currency,
                order.CapturePaymentRequestId, cancellationToken);
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net, capture.CreatedAt);
            await db.SaveChangesAsync(cancellationToken);
            return Map(order);
        }
        catch (PayPalProviderException ex)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "paypal_error", ProviderMessage(ex), ex);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<PaymentStateResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await AnyOrder(orderId, cancellationToken);
            if (order.PaymentStatus == PaymentStatus.Cancelled) return Map(order);
            if (order.PayPalCaptureId is not null || order.FulfilmentStatus == FulfilmentStatus.Fulfilled)
                throw Conflict("order_already_captured", "A captured order cannot be cancelled; refund it instead.");
            if (order.PayPalAuthorizationId is null)
                throw Conflict("order_not_authorized", "The order has no authorization to release.");

            var current = await payPal.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
            var status = current.AuthorizationStatus;
            if (status != "VOIDED")
                status = await payPal.VoidAsync(order.PayPalAuthorizationId, order.VoidPaymentRequestId, cancellationToken);
            order.RecordVoid(status);
            await db.SaveChangesAsync(cancellationToken);
            return Map(order);
        }
        catch (PayPalProviderException ex)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "paypal_error", ProviderMessage(ex), ex);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, decimal? requestedAmount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 108)
            throw BadRequest("idempotency_key_required", "A non-empty Idempotency-Key header of at most 108 characters is required.");

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrder(orderId, buyerId, cancellationToken);
            if (order.PayPalCaptureId is null || order.CapturedAmount is null)
                throw Conflict("order_not_captured", "Only a captured order can be refunded.");

            var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            if (existing?.PayPalRefundId is not null)
            {
                if (existing.Status == "PENDING")
                {
                    var refreshed = await payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken);
                    existing.RecordProviderResult(refreshed.Id, refreshed.Status, refreshed.Amount, refreshed.UpdatedAt);
                    order.RefreshRefundState();
                    await db.SaveChangesAsync(cancellationToken);
                }
                return Map(existing);
            }

            var alreadyReserved = order.Refunds
                .Where(x => x != existing && x.Status is "CREATING" or "PENDING" or "COMPLETED")
                .Sum(x => x.Amount);
            var remaining = order.CapturedAmount.Value - alreadyReserved;
            var amount = existing?.Amount ?? requestedAmount ?? remaining;
            if (amount <= 0 || amount > remaining)
                throw Conflict("refund_exceeds_capture",
                    $"The refundable balance is {remaining:0.00} {order.Currency}.");

            var refund = existing ?? order.AddRefund(idempotencyKey, amount);
            await db.SaveChangesAsync(cancellationToken);
            var provider = await payPal.RefundAsync(order.PayPalCaptureId, amount, order.Currency,
                idempotencyKey, cancellationToken);
            refund.RecordProviderResult(provider.Id, provider.Status, provider.Amount, provider.UpdatedAt);
            order.RefreshRefundState();
            await db.SaveChangesAsync(cancellationToken);
            return Map(refund);
        }
        catch (PayPalProviderException ex)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "paypal_error", ProviderMessage(ex), ex);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<IReadOnlyList<PaymentStateResponse>> MyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(Map).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ValidateCard(request.Card);
        var method = new SavedPaymentMethod(buyerId, Guid.NewGuid().ToString("N"));
        db.SavedPaymentMethods.Add(method);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var result = await payPal.SaveCardAsync(buyerId, Map(request.Card), method.CreateRequestId,
                cancellationToken);
            method.RecordVault(result.Id, result.Name, result.Brand, result.LastDigits, result.Expiry, result.Type);
            await db.SaveChangesAsync(cancellationToken);
            return Map(method);
        }
        catch (PayPalProviderException ex)
        {
            db.SavedPaymentMethods.Remove(method);
            await db.SaveChangesAsync(cancellationToken);
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "paypal_error", ProviderMessage(ex), ex);
        }
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var methods = await db.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive && x.PayPalVaultId != null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return methods.Select(Map).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == buyerId && x.IsActive, cancellationToken);
        if (method?.PayPalVaultId is null)
            throw new PaymentApiException((int)HttpStatusCode.NotFound, "payment_method_not_found",
                "The saved payment method does not exist or is no longer active.");
        try
        {
            await payPal.DeleteSavedCardAsync(method.PayPalVaultId, cancellationToken);
            method.Deactivate();
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (PayPalProviderException ex)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "paypal_error", ProviderMessage(ex), ex);
        }
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime() > now ? now : to.ToUniversalTime();
        if (fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(31) || fromUtc < now.AddYears(-3))
            throw BadRequest("invalid_date_range",
                "The range must have from before to, cannot exceed 31 days, and must fall within PayPal's three-year reporting window.");

        IReadOnlyList<PayPalTransaction> transactions;
        try
        {
            transactions = await payPal.SearchTransactionsAsync(fromUtc, toUtc, cancellationToken);
        }
        catch (PayPalProviderException ex)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "paypal_error", ProviderMessage(ex), ex);
        }

        var orders = await db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => (x.OrderDate >= fromUtc && x.OrderDate <= toUtc) ||
                        (x.CapturedAt >= fromUtc && x.CapturedAt <= toUtc) ||
                        x.Refunds.Any(refund =>
                            (refund.CreatedAt >= fromUtc && refund.CreatedAt <= toUtc) ||
                            (refund.UpdatedAt >= fromUtc && refund.UpdatedAt <= toUtc)))
            .ToListAsync(cancellationToken);
        var matchedOrderIds = new HashSet<int>();
        var items = new List<ReconciliationItem>();

        foreach (var transaction in transactions)
        {
            var order = orders.FirstOrDefault(x => TransactionMatches(transaction, x));
            if (order is not null) matchedOrderIds.Add(order.Id);
            items.Add(new ReconciliationItem(
                order is null ? "PayPalOnly" : "Matched",
                order?.Id,
                transaction.TransactionId,
                transaction.ReferenceId,
                transaction.Status,
                transaction.EventCode,
                transaction.UpdatedAt ?? transaction.InitiatedAt,
                transaction.Amount,
                transaction.Currency,
                order?.PaymentStatus.ToString(),
                order?.PayPalCaptureId));
        }

        foreach (var order in orders.Where(x => !matchedOrderIds.Contains(x.Id)))
        {
            items.Add(new ReconciliationItem("EShopOnly", order.Id, null, null, null, null,
                order.CapturedAt ?? order.OrderDate, order.CapturedAmount ?? order.Total(), order.Currency,
                order.PaymentStatus.ToString(), order.PayPalCaptureId));
        }

        return new ReconciliationResponse(fromUtc, toUtc, DateTimeOffset.UtcNow, items);
    }

    private async Task<Order> OwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        return order ?? throw new PaymentApiException((int)HttpStatusCode.NotFound, "order_not_found",
            "The order does not exist.");
    }

    private async Task<Order> AnyOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new PaymentApiException((int)HttpStatusCode.NotFound, "order_not_found",
            "The order does not exist.");
    }

    private static bool TransactionMatches(PayPalTransaction transaction, Order order)
    {
        var ids = new[] { order.PayPalOrderId, order.PayPalAuthorizationId, order.PayPalCaptureId }
            .Concat(order.Refunds.Select(x => x.PayPalRefundId))
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);
        return (transaction.TransactionId is not null && ids.Contains(transaction.TransactionId)) ||
               (transaction.ReferenceId is not null && ids.Contains(transaction.ReferenceId)) ||
               transaction.InvoiceId == order.PaymentReference || transaction.CustomId == order.PaymentReference ||
               transaction.InvoiceId == order.Id.ToString() || transaction.CustomId == order.Id.ToString();
    }

    private static PaymentStateResponse Map(Order order) => new(
        order.Id,
        order.Total(),
        order.Currency,
        order.PaymentStatus.ToString(),
        order.FulfilmentStatus.ToString(),
        order.PayPalOrderId,
        order.PayPalAuthorizationId,
        order.PayPalAuthorizationStatus,
        order.PayPalAuthorizationExpiresAt,
        order.PayPalCaptureId,
        order.PayPalCaptureStatus,
        order.CapturedAmount,
        order.PayPalFee,
        order.NetProceeds,
        order.RefundedTotal(),
        order.Refunds.Select(Map).ToList());

    private static RefundResponse Map(PaymentRefund refund) => new(
        refund.Id, refund.PayPalRefundId, refund.Amount, refund.Currency, refund.Status);

    private static PaymentMethodResponse Map(SavedPaymentMethod method) => new(
        method.Id, method.Brand, method.LastDigits, method.Expiry, method.Type, method.CardholderName);

    private static CardInput Map(CardRequestDto card)
    {
        ValidateCard(card);
        return new CardInput(card.Name, card.Number, card.Expiry, card.SecurityCode,
            card.BillingAddress is null ? null : new BillingAddressInput(
                card.BillingAddress.CountryCode,
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.City,
                card.BillingAddress.Region,
                card.BillingAddress.PostalCode));
    }

    private static void ValidateCard(CardRequestDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
            throw BadRequest("invalid_card", "Card name, number, expiry and securityCode are required.");
        if (card.BillingAddress is not null && string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw BadRequest("invalid_billing_address", "Billing address countryCode is required when an address is supplied.");
    }

    private static string ProviderMessage(PayPalProviderException exception)
    {
        var debug = string.IsNullOrWhiteSpace(exception.DebugId) ? string.Empty : $" PayPal debug ID: {exception.DebugId}.";
        return $"{exception.Message}{debug}";
    }

    private static PaymentApiException BadRequest(string code, string message) =>
        new((int)HttpStatusCode.BadRequest, code, message);

    private static PaymentApiException Conflict(string code, string message) =>
        new((int)HttpStatusCode.Conflict, code, message);
}
