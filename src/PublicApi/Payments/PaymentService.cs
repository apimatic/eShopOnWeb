using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<OrderResponse> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (request.Items.Count == 0)
        {
            throw BadRequest("At least one catalog item is required.");
        }
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            throw BadRequest("Catalog item IDs and quantities must be positive.");
        }

        ValidateShippingAddress(request.ShippingAddress);
        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => checked(x.Sum(y => y.Quantity)));
        var catalogItems = await _db.CatalogItems
            .Where(x => requestedItems.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missingIds = requestedItems.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missingIds.Length > 0)
        {
            throw BadRequest($"Unknown catalog item IDs: {string.Join(", ", missingIds)}.");
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requestedItems[item.Id])).ToList();
        var total = orderItems.Sum(x => x.UnitPrice * x.Units);
        if (total <= 0 || decimal.Round(total, 2, MidpointRounding.AwayFromZero) != total)
        {
            throw BadRequest("The order total must be positive and have no fractions smaller than one cent.");
        }

        var currency = GetCurrency();
        var address = request.ShippingAddress!;
        var order = new Order(
            buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems,
            currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return ToOrderResponse(order);
    }

    public async Task<PaymentResponse> PayAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var operationLock = await AcquireLockAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, buyerId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order was not created through the payments API.");

        if (payment.Status == OrderPaymentStatus.Authorized)
        {
            return ToPaymentResponse(payment);
        }
        if (payment.Status != OrderPaymentStatus.AwaitingPayment ||
            order.FulfillmentStatus != FulfillmentStatus.Pending)
        {
            throw Conflict($"An order in payment state {payment.Status} cannot be authorized.");
        }
        if ((request.Card is null) == !request.PaymentMethodId.HasValue)
        {
            throw BadRequest("Provide either card details or paymentMethodId, but not both.");
        }

        string? vaultId = null;
        if (request.PaymentMethodId.HasValue)
        {
            var savedMethod = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                x => x.Id == request.PaymentMethodId.Value && x.BuyerId == buyerId && x.IsActive,
                cancellationToken);
            if (savedMethod is null)
            {
                throw NotFound("Saved payment method not found.");
            }
            vaultId = savedMethod.PayPalPaymentTokenId;
        }
        else
        {
            ValidateCard(request.Card);
        }

        try
        {
            if (payment.PayPalOrderId is null)
            {
                var payPalOrderId = await _payPal.CreateOrderAsync(
                    order.Id,
                    payment.OrderAmount,
                    payment.Currency,
                    payment.InvoiceId,
                    cancellationToken);
                payment.RecordPayPalOrder(payPalOrderId);
                await _db.SaveChangesAsync(cancellationToken);
            }
            var authorizationAttempt = payment.StartAuthorizationAttempt();
            await _db.SaveChangesAsync(cancellationToken);
            var authorization = await _payPal.AuthorizeOrderAsync(
                payment.PayPalOrderId!,
                order.Id,
                payment.OrderAmount,
                payment.Currency,
                request.Card,
                vaultId,
                authorizationAttempt,
                cancellationToken);
            payment.RecordAuthorization(
                authorization.PayPalOrderId,
                authorization.AuthorizationId,
                authorization.Status,
                authorization.Amount,
                authorization.CreatedAt,
                authorization.ExpirationTime,
                authorization.Brand,
                authorization.LastDigits,
                request.PaymentMethodId);
            await _db.SaveChangesAsync(cancellationToken);
            return ToPaymentResponse(payment);
        }
        catch (PayPalApiException ex)
        {
            throw TranslatePayPalException(ex, "authorize the payment");
        }
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operationLock = await AcquireLockAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, buyerId: null, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order has no PayPal payment.");

        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled)
        {
            return ToOrderResponse(order);
        }
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
        {
            throw Conflict("A cancelled order cannot be fulfilled.");
        }

        if (payment.Status == OrderPaymentStatus.CapturePending)
        {
            var refreshed = await _payPal.GetCaptureAsync(payment.CaptureId!, cancellationToken);
            payment.UpdateCapture(refreshed.Status, refreshed.Amount, refreshed.Fee, refreshed.NetAmount, refreshed.CreatedAt);
            if (payment.Status == OrderPaymentStatus.CapturePending)
            {
                await _db.SaveChangesAsync(cancellationToken);
                return ToOrderResponse(order);
            }
            if (payment.Status == OrderPaymentStatus.CaptureFailed)
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw Conflict($"PayPal reported capture status {refreshed.Status}. Do not fulfil the order; investigate the capture in PayPal.");
            }
        }
        else if (payment.Status == OrderPaymentStatus.Authorized)
        {
            var authorization = payment.CurrentAuthorization
                ?? throw Conflict("The order has no current PayPal authorization ID.");
            var now = DateTimeOffset.UtcNow;
            if (now >= authorization.ExpirationTime)
            {
                throw Conflict(
                    "The PayPal authorization has passed its 29-day validity period and cannot be renewed. " +
                    "Cancel this order and ask the shopper to place and pay for a replacement order.");
            }

            if (now >= authorization.CreatedAt.AddDays(3))
            {
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(
                        authorization.PayPalAuthorizationId,
                        payment.PayPalOrderId!,
                        authorization.Amount,
                        authorization.Currency,
                        authorization.ExpirationTime,
                        $"eshop-reauth-{authorization.PayPalAuthorizationId}",
                        cancellationToken);
                    payment.RecordReauthorization(
                        renewed.AuthorizationId,
                        renewed.Status,
                        renewed.Amount,
                        renewed.CreatedAt,
                        renewed.ExpirationTime);
                    await _db.SaveChangesAsync(cancellationToken);
                    authorization = payment.CurrentAuthorization!;
                }
                catch (PayPalApiException ex)
                {
                    throw Conflict(
                        "PayPal could not renew the stale authorization. Do not fulfil the order; " +
                        $"cancel it and ask the shopper to place a replacement order. PayPal debug ID: {ex.DebugId ?? "not supplied"}.");
                }
            }

            try
            {
                var capture = await _payPal.CaptureAsync(
                    authorization.PayPalAuthorizationId,
                    payment.InvoiceId,
                    payment.OrderAmount,
                    payment.Currency,
                    $"eshop-capture-{authorization.PayPalAuthorizationId}",
                    cancellationToken);
                if (capture.Amount != payment.OrderAmount ||
                    !string.Equals(capture.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentApiException(HttpStatusCode.BadGateway, "PayPal captured an unexpected amount or currency.");
                }
                if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    throw Conflict($"PayPal did not complete the capture (status: {capture.Status}). Do not fulfil the order.");
                }
                if (string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                    (capture.Fee is null || capture.NetAmount is null))
                {
                    throw new PaymentApiException(HttpStatusCode.BadGateway, "PayPal completed the capture without its fee and net proceeds breakdown.");
                }

                payment.RecordCapture(
                    capture.CaptureId,
                    capture.Status,
                    capture.Amount,
                    capture.Fee,
                    capture.NetAmount,
                    capture.CreatedAt);
            }
            catch (PayPalApiException ex)
            {
                throw TranslatePayPalException(ex, "capture the authorized payment");
            }
        }
        else
        {
            throw Conflict($"An order in payment state {payment.Status} cannot be fulfilled.");
        }

        if (payment.Status == OrderPaymentStatus.Captured)
        {
            order.MarkFulfilled();
        }
        await _db.SaveChangesAsync(cancellationToken);
        return ToOrderResponse(order);
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operationLock = await AcquireLockAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, buyerId: null, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order has no PayPal payment.");

        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
        {
            return ToOrderResponse(order);
        }
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled)
        {
            throw Conflict("A fulfilled order cannot be cancelled; refund its capture instead.");
        }
        if (payment.Status == OrderPaymentStatus.Authorized)
        {
            try
            {
                var status = await _payPal.VoidAsync(
                    payment.CurrentAuthorization!.PayPalAuthorizationId,
                    cancellationToken);
                payment.RecordVoid(status);
            }
            catch (PayPalApiException ex)
            {
                throw TranslatePayPalException(ex, "void the authorization");
            }
        }
        else if (payment.Status != OrderPaymentStatus.AwaitingPayment && payment.Status != OrderPaymentStatus.Voided)
        {
            throw Conflict($"An order in payment state {payment.Status} cannot be cancelled.");
        }

        order.MarkCancelled();
        await _db.SaveChangesAsync(cancellationToken);
        return ToOrderResponse(order);
    }

    public async Task<(PaymentRefundResponse Refund, PaymentResponse Payment)> RefundAsync(
        string buyerId,
        int orderId,
        CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var operationLock = await AcquireLockAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, buyerId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order has no PayPal payment.");
        ValidateIdempotencyKey(request.IdempotencyKey);

        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            return (ToRefundResponse(existing), ToPaymentResponse(payment));
        }
        if (order.FulfillmentStatus != FulfillmentStatus.Fulfilled ||
            payment.Status is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
        {
            throw Conflict("Only a fulfilled order with a captured balance can be refunded.");
        }

        var remaining = payment.CapturedAmount!.Value - payment.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
        {
            throw BadRequest("Refund amount must be positive and have no fractions smaller than one cent.");
        }
        if (amount > remaining)
        {
            throw Conflict($"Refund amount exceeds the remaining captured balance of {remaining:0.00} {payment.Currency}.");
        }

        try
        {
            var result = await _payPal.RefundAsync(
                payment.CaptureId!,
                amount,
                payment.Currency,
                RefundRequestId(order.Id, payment.CaptureId!, request.IdempotencyKey),
                cancellationToken);
            if (result.Amount != amount ||
                !string.Equals(result.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentApiException(HttpStatusCode.BadGateway, "PayPal refunded an unexpected amount or currency.");
            }
            var refund = payment.RecordRefund(
                result.RefundId,
                result.Status,
                result.Amount,
                request.IdempotencyKey,
                result.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return (ToRefundResponse(refund), ToPaymentResponse(payment));
        }
        catch (PayPalApiException ex)
        {
            throw TranslatePayPalException(ex, "refund the capture");
        }
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await OrderQuery()
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToOrderResponse).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateCard(request.Card);
        await using var operationLock = await AcquireLockAsync($"buyer:{buyerId}", cancellationToken);
        var customerId = await _db.SavedPaymentMethods
            .Where(x => x.BuyerId == buyerId && x.PayPalCustomerId != null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.PayPalCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        var requestId = $"eshop-vault-{Guid.NewGuid():N}";

        PayPalSavedCardResult saved;
        try
        {
            saved = await _payPal.SaveCardAsync(
                request.Card!,
                MerchantCustomerId(buyerId),
                customerId,
                requestId,
                cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw TranslatePayPalException(ex, "save the card");
        }

        var method = new SavedPaymentMethod(
            buyerId,
            saved.PaymentTokenId,
            saved.CustomerId,
            saved.Brand,
            saved.LastDigits,
            saved.Expiry);
        _db.SavedPaymentMethods.Add(method);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await _payPal.DeletePaymentTokenAsync(saved.PaymentTokenId, CancellationToken.None);
            }
            catch
            {
                // Preserve the original database exception; reconciliation can identify the orphaned token.
            }
            throw;
        }
        return ToPaymentMethodResponse(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var methods = await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return methods.Select(ToPaymentMethodResponse).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var operationLock = await AcquireLockAsync($"buyer:{buyerId}", cancellationToken);
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId,
            cancellationToken);
        if (method is null)
        {
            throw NotFound("Saved payment method not found.");
        }
        if (!method.IsActive)
        {
            return;
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw TranslatePayPalException(ex, "delete the saved card");
        }
        method.Remove(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw BadRequest("The reconciliation 'to' time must be after 'from'.");
        }

        var payPalTransactions = new List<PayPalTransaction>();
        var reportFrom = TruncateToSecond(from);
        var reportTo = TruncateToSecond(to);
        var cursor = reportFrom;
        while (cursor <= reportTo)
        {
            var maximumWindowEnd = cursor.AddDays(31).AddSeconds(-1);
            var windowEnd = maximumWindowEnd < reportTo ? maximumWindowEnd : reportTo;
            var pageNumber = 1;
            int totalPages;
            do
            {
                PayPalTransactionPage page;
                try
                {
                    page = await _payPal.SearchTransactionsAsync(cursor, windowEnd, pageNumber, cancellationToken);
                }
                catch (PayPalApiException ex)
                {
                    throw TranslatePayPalException(ex, "retrieve the transaction report");
                }
                payPalTransactions.AddRange(page.Transactions);
                totalPages = Math.Max(1, page.TotalPages);
                pageNumber++;
            } while (pageNumber <= totalPages);

            if (windowEnd == reportTo)
            {
                break;
            }
            cursor = windowEnd.AddSeconds(1);
        }

        payPalTransactions = payPalTransactions
            .GroupBy(x => new { x.TransactionId, x.EventCode, x.InitiatedAt, x.Amount, x.Currency })
            .Select(x => x.First())
            .OrderBy(x => x.InitiatedAt)
            .ToList();

        var localOrders = await OrderQuery()
            .AsNoTracking()
            .Where(x => x.Payment != null &&
                (x.OrderDate >= from && x.OrderDate <= to ||
                 x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to ||
                 x.Payment.Authorizations.Any(a => a.CreatedAt >= from && a.CreatedAt <= to) ||
                 x.Payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)))
            .ToListAsync(cancellationToken);

        var localRecords = BuildLocalRecords(localOrders);
        var externalToOrder = localRecords
            .GroupBy(x => x.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().OrderId, StringComparer.OrdinalIgnoreCase);
        foreach (var order in localOrders.Where(x => x.Payment?.PayPalOrderId is not null))
        {
            externalToOrder.TryAdd(order.Payment!.PayPalOrderId!, order.Id);
            externalToOrder.TryAdd(order.Payment.InvoiceId, order.Id);
        }

        int? MatchOrder(PayPalTransaction transaction)
        {
            foreach (var key in new[] { transaction.TransactionId, transaction.ReferenceId, transaction.InvoiceId })
            {
                if (key is not null && externalToOrder.TryGetValue(key, out var orderId))
                {
                    return orderId;
                }
            }
            return null;
        }

        var reconciledPayPal = payPalTransactions.Select(x =>
        {
            var orderId = MatchOrder(x);
            return new ReconciledPayPalTransaction(
                x.TransactionId,
                x.ReferenceId,
                x.EventCode,
                x.Status,
                x.Amount,
                x.Currency,
                x.Fee,
                x.InitiatedAt,
                x.InvoiceId,
                orderId,
                orderId.HasValue ? "Matched" : "PayPalOnly");
        }).ToList();

        var matchedExternalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transaction in payPalTransactions)
        {
            matchedExternalIds.Add(transaction.TransactionId);
            if (transaction.ReferenceId is not null) matchedExternalIds.Add(transaction.ReferenceId);
            if (transaction.InvoiceId is not null) matchedExternalIds.Add(transaction.InvoiceId);
        }
        var reconciledLocal = localRecords.Select(x => x with
        {
            MatchStatus = matchedExternalIds.Contains(x.ExternalId) ? "Matched" : "EShopOnly"
        }).ToList();

        return new ReconciliationResponse(from, to, reconciledPayPal, reconciledLocal);
    }

    private IQueryable<Order> OrderQuery() => _db.Orders
        .Include(x => x.OrderItems)
        .ThenInclude(x => x.ItemOrdered)
        .Include(x => x.Payment)
        .ThenInclude(x => x!.Authorizations)
        .Include(x => x.Payment)
        .ThenInclude(x => x!.Refunds);

    private async Task<Order> GetOrderAsync(
        int orderId,
        string? buyerId,
        CancellationToken cancellationToken)
    {
        var query = OrderQuery().Where(x => x.Id == orderId);
        if (buyerId is not null)
        {
            query = query.Where(x => x.BuyerId == buyerId);
        }
        return await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound("Order not found.");
    }

    private string GetCurrency()
    {
        var currency = _options.Currency?.ToUpperInvariant() ?? string.Empty;
        if (!Regex.IsMatch(currency, "^[A-Z]{3}$"))
        {
            throw new PaymentApiException(HttpStatusCode.ServiceUnavailable, "PayPal currency configuration is missing or invalid.");
        }
        return currency;
    }

    private static void ValidateShippingAddress(ShippingAddressRequest? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw BadRequest("A complete shipping address is required.");
        }
    }

    private static void ValidateCard(CardInput? card)
    {
        if (card is null)
        {
            throw BadRequest("Card details are required.");
        }
        if (!Regex.IsMatch(card.Number ?? string.Empty, "^[0-9]{13,19}$") ||
            !Regex.IsMatch(card.Expiry ?? string.Empty, "^[0-9]{4}-(0[1-9]|1[0-2])$") ||
            !Regex.IsMatch(card.SecurityCode ?? string.Empty, "^[0-9]{3,4}$") ||
            string.IsNullOrWhiteSpace(card.Name))
        {
            throw BadRequest("Card details are invalid.");
        }
        if (!DateOnly.TryParseExact(
                $"{card.Expiry}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var expiryMonth) ||
            expiryMonth.AddMonths(1) <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw BadRequest("Card expiry must be in the future.");
        }
        var address = card.BillingAddress;
        if (address is null ||
            string.IsNullOrWhiteSpace(address.AddressLine1) ||
            string.IsNullOrWhiteSpace(address.AdminArea2) ||
            string.IsNullOrWhiteSpace(address.PostalCode) ||
            !Regex.IsMatch(address.CountryCode ?? string.Empty, "^[A-Za-z]{2}$"))
        {
            throw BadRequest("A valid card billing address is required.");
        }
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
        {
            throw BadRequest("idempotencyKey is required and cannot exceed 128 characters.");
        }
    }

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentApiException(HttpStatusCode.Unauthorized, "The bearer token has no caller identity.");
        }
    }

    private static OrderResponse ToOrderResponse(Order order)
    {
        return new OrderResponse(
            order.Id,
            order.OrderDate,
            order.Total(),
            order.Payment?.Currency ?? string.Empty,
            order.FulfillmentStatus.ToString(),
            order.Payment is null ? null : ToPaymentResponse(order.Payment),
            order.OrderItems.Select(x => new OrderLineResponse(
                x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName,
                x.UnitPrice,
                x.Units)).ToList());
    }

    private static PaymentResponse ToPaymentResponse(OrderPayment payment)
    {
        var authorization = payment.CurrentAuthorization;
        return new PaymentResponse(
            payment.Status.ToString(),
            payment.OrderAmount,
            payment.Currency,
            payment.InvoiceId,
            payment.PayPalOrderId,
            payment.FundingBrand,
            payment.FundingLastDigits,
            payment.SavedPaymentMethodId,
            authorization is null ? null : new PaymentAuthorizationResponse(
                authorization.PayPalAuthorizationId,
                authorization.Status,
                authorization.Amount,
                authorization.Currency,
                authorization.CreatedAt,
                authorization.ExpirationTime),
            payment.CaptureId is null ? null : new CaptureResponse(
                payment.CaptureId,
                payment.CaptureStatus!,
                payment.CapturedAmount,
                payment.PayPalFee,
                payment.NetAmount,
                payment.CapturedAt),
            payment.RefundedAmount,
            payment.Refunds.OrderBy(x => x.CreatedAt).Select(ToRefundResponse).ToList());
    }

    private static PaymentRefundResponse ToRefundResponse(PaymentRefund refund) => new(
        refund.PayPalRefundId,
        refund.Status,
        refund.Amount,
        refund.Currency,
        refund.CreatedAt);

    private static PaymentMethodResponse ToPaymentMethodResponse(SavedPaymentMethod method) => new(
        method.Id,
        method.Brand,
        method.LastDigits,
        method.Expiry,
        method.CreatedAt);

    private static List<ReconciledLocalRecord> BuildLocalRecords(IEnumerable<Order> orders)
    {
        var records = new List<ReconciledLocalRecord>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.PayPalOrderId is not null)
            {
                records.Add(new ReconciledLocalRecord(
                    order.Id, "Order", payment.PayPalOrderId, payment.Status.ToString(),
                    payment.OrderAmount, payment.Currency, order.OrderDate, string.Empty));
            }
            records.AddRange(payment.Authorizations.Select(x => new ReconciledLocalRecord(
                order.Id, "Authorization", x.PayPalAuthorizationId, x.Status,
                x.Amount, x.Currency, x.CreatedAt, string.Empty)));
            if (payment.CaptureId is not null)
            {
                records.Add(new ReconciledLocalRecord(
                    order.Id, "Capture", payment.CaptureId, payment.CaptureStatus!,
                    payment.CapturedAmount ?? 0m, payment.Currency, payment.CapturedAt ?? order.OrderDate, string.Empty));
            }
            records.AddRange(payment.Refunds.Select(x => new ReconciledLocalRecord(
                order.Id, "Refund", x.PayPalRefundId, x.Status,
                x.Amount, x.Currency, x.CreatedAt, string.Empty)));
        }
        return records;
    }

    private static string MerchantCustomerId(string buyerId) => $"eshop-{Hash(buyerId)[..40]}";
    private static DateTimeOffset TruncateToSecond(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Offset);
    private static string RefundRequestId(int orderId, string captureId, string key) =>
        $"eshop-{orderId}-refund-{Hash($"{captureId}:{key}")[..40]}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static PaymentApiException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    private static PaymentApiException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    private static PaymentApiException Conflict(string message) => new(HttpStatusCode.Conflict, message);

    private static PaymentApiException TranslatePayPalException(PayPalApiException exception, string action)
    {
        var status = (int)exception.StatusCode >= 500
            ? HttpStatusCode.BadGateway
            : HttpStatusCode.UnprocessableEntity;
        var issue = exception.Issues.FirstOrDefault();
        var suffix = issue is null ? string.Empty : $" Issue: {issue}.";
        var debug = exception.DebugId is null ? string.Empty : $" PayPal debug ID: {exception.DebugId}.";
        return new PaymentApiException(status, $"PayPal could not {action}.{suffix}{debug}");
    }

    private static async Task<OperationLock> AcquireLockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new OperationLock(semaphore);
    }

    private sealed class OperationLock : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public OperationLock(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public ValueTask DisposeAsync()
        {
            _semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
