using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentApplicationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;

    public PaymentApplicationService(CatalogContext db, IPayPalClient payPal, IUriComposer uriComposer)
    {
        _db = db;
        _payPal = payPal;
        _uriComposer = uriComposer;
    }

    public async Task<CreateOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var grouped = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(line => line.Quantity) })
            .ToList();
        if (grouped.Any(x => x.Quantity > 100))
        {
            throw new PaymentOperationException(400, "The combined quantity for a catalog item cannot exceed 100.");
        }

        var ids = grouped.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var missingIds = ids.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missingIds.Length > 0)
        {
            throw new PaymentOperationException(400, $"Unknown catalog item IDs: {string.Join(", ", missingIds)}.");
        }

        var lines = grouped.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            if (decimal.Round(item.Price, 2) != item.Price)
            {
                throw new PaymentOperationException(500, $"Catalog item {item.Id} has a price that cannot be charged to the cent.");
            }

            var ordered = new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri));
            return new OrderItem(ordered, item.Price, line.Quantity);
        }).ToList();

        var address = request.ShippingAddress;
        var order = new Order(buyerId, new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), lines);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new CreateOrderResponse(order.Id, order.Total(), order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString());
    }

    public async Task<PayOrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        return await WithLockAsync($"order:{orderId}", async () =>
        {
            var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
            if (order.PaymentStatus == PaymentStatus.Authorized)
            {
                return ToPayResponse(order);
            }

            CardDetails? card = request.Card?.ToCardDetails();
            string? vaultId = null;
            if (request.PaymentMethodId.HasValue)
            {
                var buyer = await _db.Buyers.Include(x => x.PaymentMethods)
                    .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, cancellationToken);
                var method = buyer?.FindActivePaymentMethod(request.PaymentMethodId.Value);
                if (method?.PayPalPaymentTokenId == null)
                {
                    throw new PaymentOperationException(404, "Payment method not found.");
                }

                vaultId = method.PayPalPaymentTokenId;
            }

            string requestId;
            try
            {
                requestId = order.BeginAuthorization(_payPal.Currency);
            }
            catch (InvalidOperationException ex)
            {
                throw new PaymentOperationException(409, ex.Message);
            }
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var result = await _payPal.AuthorizeAsync(order.Id, order.PaymentReference, order.Total(), card, vaultId, requestId, cancellationToken);
                if (!string.Equals(result.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
                {
                    order.FailAuthorization(false);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new PaymentOperationException(422, $"PayPal did not create a usable authorization (status: {result.Status}).");
                }

                order.CompleteAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.Amount, result.Currency, result.CreatedAt, result.ExpiresAt);
                await _db.SaveChangesAsync(cancellationToken);
                return ToPayResponse(order);
            }
            catch (PayPalPayerActionRequiredException ex)
            {
                order.FailAuthorization(true);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(422, ex.Message);
            }
            catch (PayPalApiException ex)
            {
                if (!ex.IsTransient)
                {
                    order.FailAuthorization(false);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                throw new PaymentOperationException(ex.IsTransient ? 503 : IsCardRejection(ex) ? 422 : 502, ex.Message);
            }
            catch (HttpRequestException)
            {
                throw new PaymentOperationException(503, "PayPal could not be reached. Retry this same payment request; the stored idempotency key prevents a duplicate authorization.");
            }
        });
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithLockAsync($"order:{orderId}", async () =>
        {
            var order = await OrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled && order.PayPalCaptureId != null)
            {
                return ToFulfilResponse(order);
            }

            if (order.PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.Capturing))
            {
                throw new PaymentOperationException(409, "The order must have an active authorization before it can be fulfilled.");
            }

            if (order.PaymentStatus == PaymentStatus.Authorized &&
                order.AuthorizationCreatedAt <= DateTimeOffset.UtcNow.AddDays(-3))
            {
                var reauthorizationRequestId = order.BeginReauthorization();
                await _db.SaveChangesAsync(cancellationToken);
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(order.PayPalAuthorizationId!, reauthorizationRequestId, cancellationToken);
                    if (!string.Equals(renewed.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PaymentOperationException(409, $"PayPal did not renew the authorization (status: {renewed.Status}). Ask the shopper to place and pay a new order.");
                    }

                    order.CompleteReauthorization(renewed.AuthorizationId, renewed.Status, renewed.Amount, renewed.Currency, renewed.CreatedAt, renewed.ExpiresAt);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (PayPalApiException ex) when (ex.IsReauthorizationUnavailable)
                {
                    throw new PaymentOperationException(409, "The authorization is outside PayPal's renewable period or is no longer renewable. Cancel this order and ask the shopper to place and pay a new order.");
                }
                catch (PayPalApiException ex)
                {
                    throw new PaymentOperationException(ex.IsTransient ? 503 : 502, ex.Message);
                }
            }

            var captureRequestId = order.BeginCapture();
            await _db.SaveChangesAsync(cancellationToken);
            try
            {
                var capture = await _payPal.CaptureAsync(order.PayPalAuthorizationId!, order.Total(), captureRequestId, cancellationToken);
                if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    order.RecordPendingCapture(capture.CaptureId, capture.Status);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new PaymentOperationException(409, $"PayPal capture {capture.CaptureId} is {capture.Status}; retry fulfilment after PayPal completes it.");
                }

                order.CompleteCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetProceeds, capture.Currency, capture.CreatedAt);
                await _db.SaveChangesAsync(cancellationToken);
                return ToFulfilResponse(order);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentOperationException(502, ex.Message);
            }
        });
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithLockAsync($"order:{orderId}", async () =>
        {
            var order = await OrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled)
            {
                return new CancelOrderResponse(order.Id, order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString());
            }

            string? requestId;
            try
            {
                requestId = order.BeginCancellation();
            }
            catch (InvalidOperationException ex)
            {
                throw new PaymentOperationException(409, ex.Message);
            }
            await _db.SaveChangesAsync(cancellationToken);

            if (requestId != null)
            {
                try
                {
                    await _payPal.VoidAsync(order.PayPalAuthorizationId!, requestId, cancellationToken);
                }
                catch (PayPalApiException ex) when (ex.Issue is "AUTHORIZATION_VOIDED" or "PREVIOUSLY_VOIDED")
                {
                    // The prior attempt succeeded but local persistence did not; converge local state.
                }
                catch (PayPalApiException ex)
                {
                    throw new PaymentOperationException(502, ex.Message);
                }

                order.CompleteCancellation("VOIDED");
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new CancelOrderResponse(order.Id, order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString());
        });
    }

    public async Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request, CancellationToken cancellationToken)
    {
        return await WithLockAsync($"order:{orderId}", async () =>
        {
            var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
            var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (existing?.Status == RefundStatus.Completed)
            {
                return ToRefundResponse(order, existing);
            }

            var completedOrPendingOther = order.Refunds
                .Where(x => x != existing && x.Status is RefundStatus.Pending or RefundStatus.Completed)
                .Sum(x => x.Amount);
            var amount = existing?.Amount ?? request.Amount ?? (order.CapturedAmount.GetValueOrDefault() - completedOrPendingOther);

            OrderRefund refund;
            try
            {
                refund = order.BeginRefund(request.IdempotencyKey, amount);
            }
            catch (InvalidOperationException ex)
            {
                throw new PaymentOperationException(409, ex.Message);
            }
            await _db.SaveChangesAsync(cancellationToken);

            var payPalRequestId = RefundRequestId(order.PayPalCaptureId!, request.IdempotencyKey);
            try
            {
                var result = await _payPal.RefundAsync(order.PayPalCaptureId!, refund.Amount, payPalRequestId, cancellationToken);
                if (string.Equals(result.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    order.CompleteRefund(refund, result.RefundId, result.Status, result.Amount, result.Currency, result.CreatedAt);
                }
                else
                {
                    order.RecordPendingRefund(refund, result.RefundId, result.Status);
                }
                await _db.SaveChangesAsync(cancellationToken);
                return ToRefundResponse(order, refund);
            }
            catch (PayPalApiException ex)
            {
                if (!ex.IsTransient)
                {
                    order.FailRefund(refund, ex.Issue ?? ex.ErrorName);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                throw new PaymentOperationException(ex.IsTransient ? 503 : ex.StatusCode == HttpStatusCode.UnprocessableEntity ? 409 : 502, ex.Message);
            }
            catch (HttpRequestException)
            {
                throw new PaymentOperationException(503, "PayPal could not be reached. Retry with the same idempotency key; it cannot create a duplicate refund.");
            }
        });
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        return await WithLockAsync($"buyer:{buyerId}", async () =>
        {
            var buyer = await _db.Buyers.Include(x => x.PaymentMethods)
                .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, cancellationToken);
            if (buyer == null)
            {
                buyer = new Buyer(buyerId);
                _db.Buyers.Add(buyer);
            }

            var method = buyer.BeginAddingPaymentMethod(Guid.NewGuid().ToString());
            await _db.SaveChangesAsync(cancellationToken);
            try
            {
                var saved = await _payPal.SaveCardAsync(card, buyer.PayPalCustomerId, method.RequestId, cancellationToken);
                buyer.CompleteAddingPaymentMethod(method, saved.PaymentTokenId, saved.CustomerId, saved.Brand, saved.Last4, saved.Expiry, saved.CardholderName);
                await _db.SaveChangesAsync(cancellationToken);
                return ToPaymentMethodResponse(method);
            }
            catch (PayPalPayerActionRequiredException ex)
            {
                throw new PaymentOperationException(422, ex.Message);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentOperationException(IsCardRejection(ex) ? 422 : 502, ex.Message);
            }
            catch (HttpRequestException)
            {
                throw new PaymentOperationException(503, "PayPal could not be reached while saving the card. Retry after connectivity is restored.");
            }
        });
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _db.Buyers.AsNoTracking().Include(x => x.PaymentMethods)
            .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, cancellationToken);
        return buyer?.PaymentMethods.Where(x => x.IsActive).Select(ToPaymentMethodResponse).ToList()
            ?? new List<PaymentMethodResponse>();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        await WithLockAsync($"buyer:{buyerId}", async () =>
        {
            var buyer = await _db.Buyers.Include(x => x.PaymentMethods)
                .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, cancellationToken);
            var method = buyer?.FindActivePaymentMethod(paymentMethodId);
            if (method?.PayPalPaymentTokenId == null)
            {
                throw new PaymentOperationException(404, "Payment method not found.");
            }

            try
            {
                await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // A previous attempt already removed it at PayPal.
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentOperationException(502, ex.Message);
            }

            buyer!.RemovePaymentMethod(method);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        });
    }

    public async Task<IReadOnlyList<OrderResponse>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(OrderResponse.FromOrder).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new PaymentOperationException(400, "from must be earlier than to.");
        }
        if (to > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            throw new PaymentOperationException(400, "to cannot be in the future.");
        }

        IReadOnlyList<PayPalTransaction> transactions;
        try
        {
            transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentOperationException(502, ex.Message);
        }

        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => x.OrderDate <= to)
            .ToListAsync(cancellationToken);
        var locals = LocalRecords(orders, from, to);
        var matchedLocal = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var transaction in transactions)
        {
            var local = locals.FirstOrDefault(x =>
                string.Equals(x.PayPalId, transaction.TransactionId, StringComparison.Ordinal) ||
                string.Equals(x.PayPalId, transaction.ReferenceId, StringComparison.Ordinal));
            var order = local?.Order ?? orders.FirstOrDefault(x => TransactionReferencesOrder(transaction, x));
            if (local != null)
            {
                matchedLocal.Add(local.Key);
            }

            entries.Add(new ReconciliationEntry(
                order == null ? "PayPalOnly" : "Matched",
                order?.Id,
                local?.Type ?? (order == null ? "None" : "OrderReference"),
                local?.PayPalId,
                transaction.TransactionId,
                transaction.ReferenceId,
                transaction.EventCode,
                transaction.Status,
                transaction.InitiatedAt,
                transaction.Amount,
                transaction.Currency,
                transaction.Fee));
        }

        entries.AddRange(locals.Where(x => !matchedLocal.Contains(x.Key)).Select(x => new ReconciliationEntry(
            "EShopOnly",
            x.Order.Id,
            x.Type,
            x.PayPalId,
            null,
            null,
            null,
            x.Status,
            x.At,
            x.Amount,
            x.Currency,
            x.Fee)));

        return new ReconciliationResponse(from, to, entries.OrderBy(x => x.TransactionTime).ThenBy(x => x.OrderId).ToList());
    }

    private async Task<Order> OwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        return order ?? throw new PaymentOperationException(404, "Order not found.");
    }

    private async Task<Order> OrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new PaymentOperationException(404, "Order not found.");
    }

    private static PayOrderResponse ToPayResponse(Order order) => new(
        order.Id, order.PaymentStatus.ToString(), order.PayPalOrderId!, order.PayPalAuthorizationId!, order.Total(), order.PaymentCurrency!, order.AuthorizationExpiresAt);

    private static FulfilOrderResponse ToFulfilResponse(Order order) => new(
        order.Id, order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString(), order.PayPalCaptureId!, order.CapturedAmount!.Value, order.PayPalFee!.Value, order.NetProceeds!.Value, order.PaymentCurrency!);

    private static RefundOrderResponse ToRefundResponse(Order order, OrderRefund refund) => new(
        refund.PayPalRefundId!, order.Id, refund.PayPalStatus ?? refund.Status.ToString(), refund.Amount, refund.Currency);

    private static PaymentMethodResponse ToPaymentMethodResponse(PaymentMethod method) => new(
        method.Id, method.Brand!, method.Last4!, method.Expiry!, method.CardholderName);

    private static bool IsCardRejection(PayPalApiException exception) =>
        exception.StatusCode == HttpStatusCode.UnprocessableEntity ||
        exception.Issue is "PAYMENT_SOURCE_DECLINED_BY_PROCESSOR" or "PAYMENT_SOURCE_CANNOT_BE_USED" or "CARD_CLOSED" or "CARD_EXPIRED";

    private static string RefundRequestId(string captureId, string callerKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{captureId}:{callerKey}"));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }

    private static bool TransactionReferencesOrder(PayPalTransaction transaction, Order order)
    {
        var reference = order.PaymentReference.ToString("N");
        return string.Equals(transaction.CustomField, reference, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(transaction.InvoiceId, $"eshop-{reference}", StringComparison.OrdinalIgnoreCase);
    }

    private static List<LocalPaymentRecord> LocalRecords(IEnumerable<Order> orders, DateTimeOffset from, DateTimeOffset to)
    {
        var records = new List<LocalPaymentRecord>();
        foreach (var order in orders)
        {
            if (order.PayPalAuthorizationId != null && order.AuthorizationCreatedAt >= from && order.AuthorizationCreatedAt <= to)
            {
                records.Add(new LocalPaymentRecord(order, "Authorization", order.PayPalAuthorizationId, order.PayPalAuthorizationStatus, order.AuthorizationCreatedAt, order.Total(), order.PaymentCurrency, null));
            }
            if (order.PayPalCaptureId != null && order.CapturedAt >= from && order.CapturedAt <= to)
            {
                records.Add(new LocalPaymentRecord(order, "Capture", order.PayPalCaptureId, order.PayPalCaptureStatus, order.CapturedAt, order.CapturedAmount, order.PaymentCurrency, order.PayPalFee));
            }
            foreach (var refund in order.Refunds.Where(x => x.PayPalRefundId != null && x.CreatedAt >= from && x.CreatedAt <= to))
            {
                records.Add(new LocalPaymentRecord(order, "Refund", refund.PayPalRefundId!, refund.PayPalStatus, refund.CompletedAt ?? refund.CreatedAt, refund.Amount, refund.Currency, null));
            }
        }
        return records;
    }

    private static async Task<T> WithLockAsync<T>(string key, Func<Task<T>> action)
    {
        var gate = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record LocalPaymentRecord(
        Order Order,
        string Type,
        string PayPalId,
        string? Status,
        DateTimeOffset? At,
        decimal? Amount,
        string? Currency,
        decimal? Fee)
    {
        public string Key => $"{Order.Id}:{Type}:{PayPalId}";
    }
}
