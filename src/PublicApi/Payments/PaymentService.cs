using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaximumAuthorizationAge = TimeSpan.FromDays(30);
    private readonly CatalogContext _context;
    private readonly IPayPalClient _payPal;
    private readonly PaymentOperationLock _operationLock;
    private readonly string _currency;

    public PaymentService(CatalogContext context, IPayPalClient payPal,
        PaymentOperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _context = context;
        _payPal = payPal;
        _operationLock = operationLock;
        _currency = options.Value.Currency.ToUpperInvariant();
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var requested = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        if (requested.Count == 0 || requested.Any(x => x.CatalogItemId <= 0 || x.Quantity is <= 0 or > 1000))
            throw BadRequest("INVALID_ORDER_ITEMS", "At least one catalog item with a valid quantity is required.");

        var ids = requested.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _context.CatalogItems.AsNoTracking()
            .Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var missing = ids.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            throw BadRequest("CATALOG_ITEMS_NOT_FOUND", $"Catalog item(s) {string.Join(", ", missing)} do not exist.");

        var orderItems = requested.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price, line.Quantity);
        }).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems, _currency);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return new CreateOrderResponse(order.Id, order.Total(), _currency,
            order.Payment!.State.ToString());
    }

    public async Task<PayOrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw BadRequest("PAYMENT_SOURCE_REQUIRED",
                "Supply exactly one payment source: card or paymentMethodId.");

        string? vaultId = null;
        if (request.PaymentMethodId.HasValue)
        {
            var buyer = await LoadBuyerAsync(buyerId, cancellationToken);
            var method = buyer?.PaymentMethods.SingleOrDefault(x => x.Id == request.PaymentMethodId.Value);
            if (method is null)
                throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved card does not exist for this shopper.");
            vaultId = method.PayPalVaultId;
        }

        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);
        var payment = RequiredPayment(order);
        if (order.FulfilmentStatus != FulfilmentStatus.Pending)
            throw Conflict("ORDER_NOT_PAYABLE", "Only a pending order can be paid.");
        if (payment.State is PaymentState.Authorized or PaymentState.CapturePending or
            PaymentState.Captured or PaymentState.PartiallyRefunded or PaymentState.Refunded)
            return new PayOrderResponse(order.Id, ToPaymentDto(payment));

        if (!payment.AuthorizationAttemptInProgress)
        {
            payment.BeginAuthorizationAttempt();
            await _context.SaveChangesAsync(cancellationToken);
        }
        var attempt = payment.AuthorizationAttempt;
        var requestPrefix = $"eshop-{order.Id}-pay-{attempt}";

        try
        {
            if (payment.PayPalOrderId is null)
            {
                var payPalOrder = await _payPal.CreateOrderAsync(order.Id, payment.InvoiceId,
                    payment.ReferenceId, order.Total(), payment.Currency,
                    order.OrderItems.Select(x => new PayPalOrderItem(x.ItemOrdered.CatalogItemId,
                        x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray(),
                    requestPrefix + "-order", cancellationToken);
                payment.RecordPayPalOrder(payPalOrder.Id, payPalOrder.Status);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var authorization = await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!,
                request.Card is null ? null : ToPayPalCard(request.Card), vaultId,
                requestPrefix + "-authorize", cancellationToken);
            if (authorization.Amount != order.Total() ||
                !string.Equals(authorization.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            {
                await BestEffortVoidAsync(authorization.Id, requestPrefix + "-amount-mismatch", cancellationToken);
                payment.RecordFailure("PAYPAL_AMOUNT_MISMATCH",
                    "PayPal authorized an amount or currency that does not equal the order total.");
                await _context.SaveChangesAsync(cancellationToken);
                throw new PaymentApiException(502, "PAYPAL_AMOUNT_MISMATCH",
                    "The authorization was released because PayPal did not hold the exact order total.");
            }

            payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
                authorization.CreatedAt, authorization.ExpiresAt);
            if (authorization.Status != "CREATED")
            {
                payment.RecordFailure("PAYPAL_AUTHORIZATION_NOT_CREATED",
                    $"PayPal returned authorization status {authorization.Status}.");
                await _context.SaveChangesAsync(cancellationToken);
                throw new PaymentApiException(409, "PAYPAL_AUTHORIZATION_NOT_CREATED",
                    $"PayPal did not create the hold; its current status is {authorization.Status}.");
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new PayOrderResponse(order.Id, ToPaymentDto(payment));
        }
        catch (PayPalApiException exception)
        {
            if ((int)exception.StatusCode < 500 && exception.StatusCode != HttpStatusCode.TooManyRequests)
            {
                payment.RecordFailure(exception.Code, PayPalMessage(exception));
                await _context.SaveChangesAsync(cancellationToken);
            }
            throw TranslatePayPal(exception, "authorize the order");
        }
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, null, cancellationToken);
        var payment = RequiredPayment(order);
        if (order.FulfilmentStatus == FulfilmentStatus.Cancelled)
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be fulfilled.");
        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled && payment.State is
            PaymentState.Captured or PaymentState.PartiallyRefunded or PaymentState.Refunded)
            return new FulfilOrderResponse(order.Id, order.FulfilmentStatus.ToString(), ToPaymentDto(payment));
        if (payment.AuthorizationId is null || payment.State is not
            (PaymentState.Authorized or PaymentState.CapturePending))
            throw Conflict("ORDER_NOT_AUTHORIZED", "The order must have an active authorization before fulfilment.");

        try
        {
            PayPalCaptureResult capture;
            if (payment.CaptureId is not null)
            {
                capture = await _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken);
            }
            else
            {
                var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId,
                    cancellationToken);
                payment.SynchronizeAuthorization(authorization.Status, authorization.ExpiresAt);

                if (authorization.Status == "PENDING")
                    throw Conflict("AUTHORIZATION_PENDING",
                        "PayPal is reviewing the authorization. Retry fulfilment after it reaches CREATED.");
                if (authorization.Status is "DENIED" or "VOIDED")
                    throw Conflict("AUTHORIZATION_UNUSABLE",
                        $"PayPal reports the authorization as {authorization.Status}. Ask the shopper to pay again.");

                var originalCreated = payment.OriginalAuthorizationCreatedAt ?? authorization.CreatedAt;
                if (originalCreated.HasValue && DateTimeOffset.UtcNow - originalCreated.Value >= MaximumAuthorizationAge)
                    throw Conflict("AUTHORIZATION_TOO_OLD_TO_RENEW",
                        "The original authorization is 30 days old and PayPal can no longer renew it. Ask the shopper to pay again.");

                var currentCreated = authorization.CreatedAt ?? payment.AuthorizationCreatedAt;
                if (authorization.Status == "CREATED" && currentCreated.HasValue &&
                    DateTimeOffset.UtcNow - currentCreated.Value >= AuthorizationHonorPeriod)
                {
                    try
                    {
                        authorization = await _payPal.ReauthorizeAsync(payment.AuthorizationId,
                            order.Total(), payment.Currency,
                            $"eshop-{order.Id}-reauthorize-{payment.ReauthorizationCount + 1}",
                            cancellationToken);
                    }
                    catch (PayPalApiException exception) when ((int)exception.StatusCode is 404 or 422)
                    {
                        throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                            $"PayPal can no longer renew this authorization. Ask the shopper to pay again. {PayPalMessage(exception)}");
                    }
                    payment.RecordAuthorization(authorization.Id, authorization.Status,
                        authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt, true);
                    await _context.SaveChangesAsync(cancellationToken);
                }

                capture = await _payPal.CaptureAsync(payment.AuthorizationId, order.Total(),
                    payment.Currency, payment.InvoiceId, $"eshop-{order.Id}-capture-1",
                    cancellationToken);
            }

            if (capture.Amount != order.Total() ||
                !string.Equals(capture.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
                throw new PaymentApiException(502, "PAYPAL_CAPTURE_AMOUNT_MISMATCH",
                    "PayPal's capture amount does not equal the order total; investigate before shipping.");

            payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
                capture.NetAmount, capture.CreatedAt);
            if (capture.Status == "COMPLETED")
            {
                order.MarkFulfilled();
            }
            else if (capture.Status is "DECLINED" or "FAILED")
            {
                payment.RecordFailure("PAYPAL_CAPTURE_FAILED",
                    $"PayPal returned capture status {capture.Status}.");
            }
            await _context.SaveChangesAsync(cancellationToken);

            if (capture.Status is "DECLINED" or "FAILED")
                throw Conflict("PAYPAL_CAPTURE_FAILED",
                    $"PayPal could not capture the authorization (status {capture.Status}); do not ship the order.");

            return new FulfilOrderResponse(order.Id, order.FulfilmentStatus.ToString(), ToPaymentDto(payment));
        }
        catch (PayPalApiException exception)
        {
            throw TranslatePayPal(exception, "capture the authorized payment");
        }
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, null, cancellationToken);
        var payment = RequiredPayment(order);
        if (order.FulfilmentStatus == FulfilmentStatus.Cancelled)
            return new CancelOrderResponse(order.Id, order.FulfilmentStatus.ToString(), ToPaymentDto(payment));
        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled || payment.CaptureId is not null)
            throw Conflict("ORDER_ALREADY_CAPTURED", "A captured order cannot be cancelled; refund it instead.");

        if (payment.AuthorizationId is not null && payment.State == PaymentState.Authorized)
        {
            try
            {
                await _payPal.VoidAsync(payment.AuthorizationId, $"eshop-{order.Id}-void-1",
                    cancellationToken);
            }
            catch (PayPalApiException exception) when ((int)exception.StatusCode is 404 or 422)
            {
                var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId,
                    cancellationToken);
                if (authorization.Status != "VOIDED" &&
                    !(authorization.ExpiresAt.HasValue && authorization.ExpiresAt <= DateTimeOffset.UtcNow))
                    throw TranslatePayPal(exception, "release the authorization");
            }
            payment.RecordVoid("VOIDED");
        }

        order.Cancel();
        await _context.SaveChangesAsync(cancellationToken);
        return new CancelOrderResponse(order.Id, order.FulfilmentStatus.ToString(), ToPaymentDto(payment));
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw BadRequest("IDEMPOTENCY_KEY_REQUIRED", "A non-empty idempotencyKey is required.");
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);
        var payment = RequiredPayment(order);
        if (order.FulfilmentStatus != FulfilmentStatus.Fulfilled || payment.CaptureId is null ||
            payment.CapturedAmount is null)
            throw Conflict("ORDER_NOT_REFUNDABLE", "Only a fulfilled, captured order can be refunded.");

        var existing = payment.FindRefund(request.IdempotencyKey);
        if (existing is not null)
        {
            if (request.Amount.HasValue && request.Amount.Value != existing.Amount)
                throw Conflict("IDEMPOTENCY_KEY_REUSED",
                    "This idempotency key was already used with a different refund amount.");
            if (existing.Status == "FAILED")
                throw Conflict(existing.FailureCode ?? "REFUND_FAILED",
                    existing.FailureMessage ?? "The prior refund request failed; use a new key after correcting it.");
            if (existing.Status != "INITIATED")
                return ToRefundResponse(existing);
        }

        var refundable = payment.CapturedAmount.Value - payment.RefundedAmount;
        var amount = existing?.Amount ?? request.Amount ?? refundable;
        if (amount <= 0 || amount > refundable || decimal.Round(amount, 2) != amount)
            throw Conflict("REFUND_EXCEEDS_REMAINING_CAPTURE",
                $"Use a positive amount with at most two decimal places, no greater than {refundable.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency}.");

        var refund = existing ?? payment.StartRefund(Guid.NewGuid(), request.IdempotencyKey, amount);
        if (existing is null) await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await _payPal.RefundAsync(payment.CaptureId, refund.Amount, payment.Currency,
                payment.InvoiceId, payment.ReferenceId, request.Note,
                $"eshop-refund-{refund.RefundId:N}", cancellationToken);
            refund.RecordPayPalResult(result.Id, result.Status, result.Amount,
                result.PayPalFeeRefunded, result.NetAmountDebited);
            payment.RefreshRefundState();
            await _context.SaveChangesAsync(cancellationToken);
            if (result.Status is "FAILED" or "CANCELLED")
                throw Conflict("PAYPAL_REFUND_FAILED",
                    $"PayPal returned refund status {result.Status}; the remaining amount is still refundable with a new key.");
            return ToRefundResponse(refund);
        }
        catch (PayPalApiException exception)
        {
            if ((int)exception.StatusCode < 500 && exception.StatusCode != HttpStatusCode.TooManyRequests)
            {
                refund.RecordFailure(exception.Code, PayPalMessage(exception));
                await _context.SaveChangesAsync(cancellationToken);
            }
            throw TranslatePayPal(exception, "refund the capture");
        }
    }

    public async Task<IReadOnlyCollection<OrderDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _context.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToOrderDto).ToArray();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Alias))
            throw BadRequest("PAYMENT_METHOD_ALIAS_REQUIRED", "A non-empty card alias is required.");
        using var operation = await _operationLock.AcquireAsync($"buyer:{buyerId}", cancellationToken);
        var result = await _payPal.CreatePaymentTokenAsync(MerchantCustomerId(buyerId),
            ToPayPalCard(request.Card), $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);
        try
        {
            var buyer = await LoadBuyerAsync(buyerId, cancellationToken);
            if (buyer is null)
            {
                buyer = new Buyer(buyerId);
                _context.Buyers.Add(buyer);
            }
            var method = buyer.AddPaymentMethod(request.Alias.Trim(), result.Id, result.Brand,
                result.Last4, result.Expiry);
            await _context.SaveChangesAsync(cancellationToken);
            return ToPaymentMethodResponse(method);
        }
        catch
        {
            try { await _payPal.DeletePaymentTokenAsync(result.Id, cancellationToken); }
            catch { /* Preserve the original persistence error; the token is unusable without a local owner mapping. */ }
            throw;
        }
    }

    public async Task<IReadOnlyCollection<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var buyer = await LoadBuyerAsync(buyerId, cancellationToken, true);
        return buyer?.PaymentMethods.Select(ToPaymentMethodResponse).ToArray() ??
               Array.Empty<PaymentMethodResponse>();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"buyer:{buyerId}", cancellationToken);
        var buyer = await LoadBuyerAsync(buyerId, cancellationToken);
        var method = buyer?.PaymentMethods.SingleOrDefault(x => x.Id == paymentMethodId);
        if (buyer is null || method is null)
            throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved card does not exist for this shopper.");
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired remote state is already true; remove the stale local mapping.
        }
        buyer.RemovePaymentMethod(method);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (to <= from) throw BadRequest("INVALID_DATE_RANGE", "to must be later than from.");

        var payPalTransactions = new Dictionary<string, PayPalTransactionResult>();
        var cursor = from;
        while (cursor < to)
        {
            var chunkEnd = cursor.AddDays(31) < to ? cursor.AddDays(31) : to;
            var page = 1;
            var totalPages = 1;
            do
            {
                var result = await _payPal.ListTransactionsAsync(cursor, chunkEnd, page, 500,
                    cancellationToken);
                totalPages = Math.Max(1, result.TotalPages);
                foreach (var transaction in result.Transactions)
                {
                    var key = $"{transaction.Id}|{transaction.EventCode}|{transaction.InitiatedAt:O}|{transaction.Amount}";
                    payPalTransactions[key] = transaction;
                }
                page++;
            } while (page <= totalPages);
            cursor = chunkEnd;
        }

        var orders = await _context.Orders.AsNoTracking()
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .Where(x => x.Payment != null)
            .ToListAsync(cancellationToken);

        var transactionDtos = payPalTransactions.Values.Select(transaction =>
        {
            var order = orders.FirstOrDefault(x => Matches(x.Payment!, transaction));
            return new ReconciliationTransactionDto(transaction.Id, transaction.ReferenceId,
                transaction.EventCode, transaction.Status, transaction.InitiatedAt, transaction.Amount,
                transaction.Currency, transaction.Fee, transaction.InvoiceId, transaction.CustomId,
                order?.Id, order is null ? "PayPalOnly" : "Matched");
        }).OrderBy(x => x.InitiatedAt).ToArray();

        var missing = new List<ReconciliationMissingLocalRecordDto>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.CaptureId is not null && payment.CapturedAmount.HasValue)
            {
                var occurredAt = payment.CaptureCreatedAt ?? order.OrderDate;
                if (occurredAt >= from && occurredAt <= to &&
                    !payPalTransactions.Values.Any(x => TransactionHasId(x, payment.CaptureId)))
                {
                    missing.Add(new ReconciliationMissingLocalRecordDto(order.Id, "Capture",
                        payment.CaptureId, occurredAt, payment.CapturedAmount.Value, payment.Currency,
                        payment.CaptureStatus ?? "UNKNOWN"));
                }
            }

            foreach (var refund in payment.Refunds.Where(x => x.PayPalRefundId is not null &&
                         x.CreatedAt >= from && x.CreatedAt <= to))
            {
                if (!payPalTransactions.Values.Any(x => TransactionHasId(x, refund.PayPalRefundId!)))
                {
                    missing.Add(new ReconciliationMissingLocalRecordDto(order.Id, "Refund",
                        refund.PayPalRefundId!, refund.CreatedAt, refund.Amount, refund.Currency,
                        refund.Status));
                }
            }
        }

        return new ReconciliationResponse(from, to, transactionDtos, missing);
    }

    private async Task<Order> LoadOrderAsync(int orderId, string? buyerId,
        CancellationToken cancellationToken)
    {
        var query = _context.Orders.Include(x => x.OrderItems)
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .Where(x => x.Id == orderId);
        if (buyerId is not null) query = query.Where(x => x.BuyerId == buyerId);
        return await query.SingleOrDefaultAsync(cancellationToken) ??
               throw NotFound("ORDER_NOT_FOUND", "The order does not exist or does not belong to this shopper.");
    }

    private Task<Buyer?> LoadBuyerAsync(string buyerId, CancellationToken cancellationToken,
        bool noTracking = false)
    {
        IQueryable<Buyer> query = _context.Buyers.Include(x => x.PaymentMethods);
        if (noTracking) query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, cancellationToken);
    }

    private async Task BestEffortVoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        try { await _payPal.VoidAsync(authorizationId, requestId, cancellationToken); }
        catch { /* The mismatch is still reported; PayPal IDs are retained in its own audit trail. */ }
    }

    private static bool Matches(OrderPayment payment, PayPalTransactionResult transaction)
    {
        if (transaction.InvoiceId == payment.InvoiceId || transaction.CustomId == payment.ReferenceId)
            return true;
        var ids = new[] { payment.PayPalOrderId, payment.AuthorizationId, payment.CaptureId }
            .Concat(payment.Refunds.Select(x => x.PayPalRefundId))
            .Where(x => x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Contains(transaction.Id) ||
               transaction.ReferenceId is not null && ids.Contains(transaction.ReferenceId);
    }

    private static bool TransactionHasId(PayPalTransactionResult transaction, string payPalId) =>
        string.Equals(transaction.Id, payPalId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(transaction.ReferenceId, payPalId, StringComparison.OrdinalIgnoreCase);

    private static OrderPayment RequiredPayment(Order order) => order.Payment ??
        throw Conflict("PAYMENT_NOT_ENABLED", "This legacy order was not created through the payment API.");

    private static PayPalCard ToPayPalCard(CardRequest card)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        if (digits.Length is < 13 or > 19 || card.Number.Any(x => !char.IsDigit(x) && x != ' '))
            throw BadRequest("INVALID_CARD_NUMBER", "The card number format is invalid.");
        if (card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(x => !char.IsDigit(x)))
            throw BadRequest("INVALID_SECURITY_CODE", "The card security code format is invalid.");
        if (!DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateTime.UtcNow.Date)
            throw BadRequest("INVALID_CARD_EXPIRY", "The card expiry must be a future year and month.");
        if (card.BillingAddress.CountryCode.Length != 2)
            throw BadRequest("INVALID_COUNTRY_CODE", "The billing countryCode must contain two letters.");

        return new PayPalCard(card.Name, digits, card.Expiry, card.SecurityCode,
            new PayPalAddress(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
                card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode));
    }

    private static PaymentMethodResponse ToPaymentMethodResponse(PaymentMethod method) =>
        new(method.Id, method.Alias, method.Brand, method.Last4, method.Expiry);

    private static RefundResponse ToRefundResponse(PaymentRefund refund) =>
        new(refund.RefundId.ToString(), refund.Status, refund.Amount, refund.Currency);

    private static OrderDto ToOrderDto(Order order) => new(order.Id, order.OrderDate, order.Total(),
        order.FulfilmentStatus.ToString(), order.OrderItems.Select(x => new OrderItemDto(
            x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray(),
        order.Payment is null ? null : ToPaymentDto(order.Payment));

    internal static PaymentDto ToPaymentDto(OrderPayment payment) => new(payment.State.ToString(),
        payment.Currency, payment.PayPalOrderId, payment.PayPalOrderStatus, payment.AuthorizationId,
        payment.AuthorizationStatus, payment.AuthorizedAmount, payment.AuthorizationExpiresAt,
        payment.CaptureId, payment.CaptureStatus, payment.CapturedAmount, payment.PayPalFee,
        payment.NetAmount, payment.RefundedAmount, payment.FailureCode, payment.FailureMessage,
        payment.Refunds.Select(x => new RefundDto(x.RefundId.ToString(), x.PayPalRefundId, x.Status,
            x.Amount, x.Currency, x.PayPalFeeRefunded, x.NetAmountDebited)).ToArray());

    private static string MerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    private static PaymentApiException TranslatePayPal(PayPalApiException exception, string action)
    {
        if (exception.RequiresPayerAction)
            return Conflict("PAYPAL_PAYER_ACTION_REQUIRED",
                "PayPal requires browser approval or a card challenge. This API intentionally does not implement an approval round-trip.");
        var status = (int)exception.StatusCode >= 500 || exception.StatusCode == HttpStatusCode.TooManyRequests
            ? 502
            : 422;
        return new PaymentApiException(status, exception.Code,
            $"PayPal could not {action}. {PayPalMessage(exception)}");
    }

    private static string PayPalMessage(PayPalApiException exception) => exception.DebugId is null
        ? exception.Message
        : $"{exception.Message} PayPal debug ID: {exception.DebugId}.";

    private static PaymentApiException BadRequest(string code, string message) => new(400, code, message);
    private static PaymentApiException NotFound(string code, string message) => new(404, code, message);
    private static PaymentApiException Conflict(string code, string message) => new(409, code, message);
}
