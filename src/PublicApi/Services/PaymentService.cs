using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim IdentityLock = new(1, 1);

    private readonly CatalogContext _db;
    private readonly FinixPaymentGateway _gateway;
    private readonly FinixOptions _options;

    public PaymentService(CatalogContext db, FinixPaymentGateway gateway, IOptions<FinixOptions> options)
    {
        _db = db;
        _gateway = gateway;
        _options = options.Value;
    }

    public async Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentValidationException("At least one order item is required.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var item in request.Items)
        {
            if (item.CatalogItemId <= 0)
            {
                throw new PaymentValidationException("Every order item must reference a valid catalog item id.");
            }

            if (item.Quantity <= 0)
            {
                throw new PaymentValidationException("Every order item quantity must be greater than zero.");
            }

            quantities[item.CatalogItemId] = quantities.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
        }

        var catalogItemIds = quantities.Keys.ToList();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != catalogItemIds.Count)
        {
            var missing = catalogItemIds.Except(catalogItems.Select(item => item.Id));
            throw new PaymentValidationException($"Unknown catalog item ids: {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in quantities)
        {
            var catalogItem = catalogItems.First(item => item.Id == catalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var orderedItem = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(orderedItem, catalogItem.Price, quantity));
        }

        var order = new Order(buyerId, MapAddress(request.ShippingAddress), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return MapOrder(order, Array.Empty<PaymentRefund>());
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(order => order.Id).ToList();
        var refunds = await _db.PaymentRefunds
            .Where(refund => orderIds.Contains(refund.OrderId))
            .ToListAsync(cancellationToken);

        return orders
            .Select(order => MapOrder(order, refunds.Where(refund => refund.OrderId == order.Id)))
            .ToList();
    }

    public async Task<PayOrderResponse> PayOrderAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentNotFoundException($"Order {orderId} was not found.");
            }

            if (order.PaymentStatus is OrderPaymentStatus.Authorized
                or OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return MapPayResponse(order);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                throw new PaymentConflictException($"Order {orderId} was canceled and can no longer be paid.");
            }

            if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            {
                throw new PaymentConflictException($"Order {orderId} is not awaiting payment.");
            }

            var savedPaymentMethodId = request.PaymentMethodId ?? request.SavedPaymentMethodId;
            if (savedPaymentMethodId is null && request.Card is null)
            {
                throw new PaymentValidationException("Provide either a saved payment method id or card details.");
            }

            string source;
            if (savedPaymentMethodId is not null)
            {
                var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
                    paymentMethod => paymentMethod.Id == savedPaymentMethodId.Value
                                     && paymentMethod.BuyerId == buyerId,
                    cancellationToken);

                if (saved is null)
                {
                    throw new PaymentNotFoundException($"Saved payment method {savedPaymentMethodId.Value} was not found.");
                }

                source = saved.FinixPaymentInstrumentId;
            }
            else
            {
                ValidateCard(request.Card!);
                var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
                var paymentInstrument = await _gateway.CreateCardPaymentInstrumentAsync(
                    identityId,
                    MapCard(request.Card!),
                    cancellationToken);
                source = paymentInstrument.Id!;
            }

            var amountCents = ToCents(order.Total());
            if (amountCents <= 0)
            {
                throw new PaymentValidationException("The order total must be greater than zero.");
            }

            var idempotencyId = order.PendingAuthorizationIdempotencyKey;
            if (string.IsNullOrWhiteSpace(idempotencyId))
            {
                idempotencyId = $"order-{order.Id}-authorization-{Guid.NewGuid():N}";
                order.SetPendingAuthorizationIdempotencyKey(idempotencyId);
                await _db.SaveChangesAsync(cancellationToken);
            }

            FinixApiReference.Models.Authorization authorization;
            try
            {
                authorization = await _gateway.CreateAuthorizationAsync(
                    source,
                    amountCents,
                    idempotencyId,
                    null,
                    cancellationToken);
            }
            catch (FinixGatewayException)
            {
                var existing = await _gateway.FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken);
                if (existing is null)
                {
                    throw;
                }

                authorization = existing;
            }

            var authorizationState = authorization.State?.Value ?? "UNKNOWN";
            if (!string.Equals(authorizationState, "SUCCEEDED", StringComparison.Ordinal))
            {
                order.ClearPendingAuthorizationIdempotencyKey();
                await _db.SaveChangesAsync(cancellationToken);

                var detail = string.IsNullOrWhiteSpace(authorization.FailureMessage)
                    ? string.Empty
                    : $" Finix said: {authorization.FailureMessage}";
                throw new PaymentConflictException(
                    $"Finix did not authorize order {order.Id}; the authorization is {authorizationState}.{detail}");
            }

            if (authorization.Amount != amountCents)
            {
                if (!string.IsNullOrWhiteSpace(authorization.Id))
                {
                    try
                    {
                        await _gateway.VoidAuthorizationAsync(authorization.Id!, cancellationToken);
                    }
                    catch (FinixGatewayException)
                    {
                    }
                }

                order.ClearPendingAuthorizationIdempotencyKey();
                await _db.SaveChangesAsync(cancellationToken);

                throw new PaymentConflictException(
                    $"Finix authorized {authorization.Amount} cents but order {order.Id} requires {amountCents} cents. " +
                    "The hold was released; the shopper should try another card.");
            }

            if (string.IsNullOrWhiteSpace(authorization.Id))
            {
                throw new FinixGatewayException("Finix returned an authorization without an id.", HttpStatusCode.BadGateway);
            }

            order.MarkAuthorized(
                authorization.Id!,
                authorizationState,
                authorization.Amount ?? amountCents,
                authorization.Currency?.Value ?? _gateway.CurrencyCode,
                source);

            await _db.SaveChangesAsync(cancellationToken);
            return MapPayResponse(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FulfilOrderResponse> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.PaymentStatus is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                if (!string.IsNullOrWhiteSpace(order.FinixCaptureTransferId)
                    && (!string.Equals(order.FinixCaptureState, "SUCCEEDED", StringComparison.Ordinal)
                        || order.FinixFeeCents is null))
                {
                    await AdoptCaptureAsync(order, order.FinixCaptureTransferId!, cancellationToken);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                return MapFulfilResponse(order);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                throw new PaymentConflictException($"Order {orderId} was canceled and cannot be fulfilled.");
            }

            if (order.PaymentStatus == OrderPaymentStatus.AwaitingPayment)
            {
                throw new PaymentConflictException($"Order {orderId} has not been paid yet; authorize payment before fulfilment.");
            }

            if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                throw new PaymentConflictException($"Order {orderId} has no Finix authorization to capture.");
            }

            var authorization = await _gateway.GetAuthorizationAsync(order.FinixAuthorizationId!, cancellationToken);

            if (!string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                await AdoptCaptureAsync(order, authorization.Transfer!, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return MapFulfilResponse(order);
            }

            var expired = authorization.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow;
            if (string.Equals(authorization.State?.Value, "CANCELED", StringComparison.Ordinal)
                || string.Equals(authorization.State?.Value, "FAILED", StringComparison.Ordinal)
                || expired)
            {
                order.UpdateAuthorizationState(authorization.State?.Value ?? "UNKNOWN");
                await _db.SaveChangesAsync(cancellationToken);

                var expiryText = authorization.ExpiresAt is { } expiry
                    ? $" It expired at {expiry:O}."
                    : string.Empty;
                throw new PaymentConflictException(
                    $"The card hold for order {order.Id} can no longer be captured " +
                    $"(Finix authorization state: {authorization.State?.Value ?? "UNKNOWN"}).{expiryText} " +
                    "Cancel the order and ask the shopper to authorize a new payment.");
            }

            var captureAmount = order.AuthorizedAmountCents ?? ToCents(order.Total());
            FinixApiReference.Models.Authorization capture;
            try
            {
                capture = await _gateway.CaptureAuthorizationAsync(
                    order.FinixAuthorizationId!,
                    captureAmount,
                    cancellationToken);
            }
            catch (FinixGatewayException exception)
            {
                var refreshed = await TryGetAuthorizationAsync(order.FinixAuthorizationId!, cancellationToken);
                if (!string.IsNullOrWhiteSpace(refreshed?.Transfer))
                {
                    await AdoptCaptureAsync(order, refreshed!.Transfer!, cancellationToken);
                    await _db.SaveChangesAsync(cancellationToken);
                    return MapFulfilResponse(order);
                }

                order.UpdateAuthorizationState(refreshed?.State?.Value ?? "UNKNOWN");
                await _db.SaveChangesAsync(cancellationToken);

                throw new PaymentConflictException(
                    $"Finix could not capture order {order.Id} " +
                    $"(authorization state: {refreshed?.State?.Value ?? "UNKNOWN"}). " +
                    "The hold may have expired or been released. Cancel the order and ask the shopper to authorize a new payment. " +
                    $"Finix detail: {exception.Message}");
            }

            if (string.IsNullOrWhiteSpace(capture.Transfer))
            {
                capture = await _gateway.GetAuthorizationAsync(order.FinixAuthorizationId!, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(capture.Transfer))
            {
                throw new PaymentConflictException(
                    $"Finix accepted the capture for order {order.Id} but did not return a transfer id. " +
                    "Re-run fulfilment after checking the Finix authorization; do not authorize the shopper again.");
            }

            await AdoptCaptureAsync(order, capture.Transfer!, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return MapFulfilResponse(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CancelOrderResponse> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                return MapCancelResponse(order);
            }

            if (order.PaymentStatus is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} was already captured. Use the refunds endpoint to return captured money.");
            }

            if (order.PaymentStatus == OrderPaymentStatus.Authorized)
            {
                if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
                {
                    throw new PaymentConflictException($"Order {orderId} has no Finix authorization to release.");
                }

                try
                {
                    var authorization = await _gateway.VoidAuthorizationAsync(order.FinixAuthorizationId!, cancellationToken);
                    order.MarkCanceled(ResolveVoidState(authorization));
                }
                catch (FinixGatewayException exception)
                {
                    var refreshed = await TryGetAuthorizationAsync(order.FinixAuthorizationId!, cancellationToken);
                    if (refreshed is not null
                        && (refreshed.VoidState is not null
                            || string.Equals(refreshed.State?.Value, "CANCELED", StringComparison.Ordinal)))
                    {
                        order.MarkCanceled(ResolveVoidState(refreshed));
                    }
                    else
                    {
                        throw new PaymentConflictException(
                            $"Finix could not release the hold for order {orderId}. " +
                            "Retry cancel; if the hold has expired, it will release automatically. " +
                            $"Finix detail: {exception.Message}");
                    }
                }
            }
            else
            {
                order.MarkCanceled(null);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return MapCancelResponse(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResponse> RefundOrderAsync(
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("An idempotency key is required for refunds.");
        }

        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order.PaymentStatus is not (OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has not been fulfilled. Refunds are only available after capture.");
            }

            if (string.IsNullOrWhiteSpace(order.FinixCaptureTransferId))
            {
                throw new PaymentConflictException($"Order {orderId} has no captured Finix transfer to refund.");
            }

            var refunds = await _db.PaymentRefunds
                .Where(refund => refund.OrderId == orderId)
                .ToListAsync(cancellationToken);

            var existing = refunds.FirstOrDefault(
                refund => string.Equals(refund.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
            if (existing is not null)
            {
                return MapRefundResponse(order, existing, refunds);
            }

            var capturedCents = order.CapturedAmountCents ?? 0;
            var refundedCents = refunds.Sum(refund => refund.AmountCents);
            var refundableCents = capturedCents - refundedCents;

            long amountCents;
            if (request.AmountCents is > 0)
            {
                amountCents = request.AmountCents.Value;
            }
            else if (request.Amount is > 0)
            {
                amountCents = ToCents(request.Amount.Value);
            }
            else
            {
                amountCents = refundableCents;
            }

            if (amountCents <= 0)
            {
                throw new PaymentValidationException("The refund amount must be greater than zero.");
            }

            if (amountCents > refundableCents)
            {
                throw new PaymentConflictException(
                    $"Refund of {amountCents} cents exceeds the remaining refundable amount of " +
                    $"{refundableCents} cents for order {orderId}.");
            }

            if (amountCents < capturedCents)
            {
                var captureSucceeded = await EnsureCaptureSucceededAsync(order, cancellationToken);
                if (!captureSucceeded)
                {
                    throw new PaymentConflictException(
                        $"Finix is still finalizing the capture for order {orderId}. " +
                        "Partial refunds are available once the capture succeeds; retry shortly, " +
                        "or refund the full captured amount now.");
                }
            }

            FinixApiReference.Models.Transfer refundTransfer;
            try
            {
                refundTransfer = await _gateway.CreateRefundAsync(
                    order.FinixCaptureTransferId!,
                    amountCents,
                    request.IdempotencyKey,
                    cancellationToken);
            }
            catch (FinixGatewayException exception)
            {
                var existingTransfer = await _gateway.FindRefundByIdempotencyAsync(
                    order.FinixCaptureTransferId!,
                    request.IdempotencyKey,
                    cancellationToken);

                if (existingTransfer is null)
                {
                    if (exception.StatusCode == HttpStatusCode.UnprocessableEntity)
                    {
                        throw new PaymentConflictException(
                            $"Finix rejected the refund for order {orderId}: {exception.Message}");
                    }

                    throw;
                }

                refundTransfer = existingTransfer;
            }

            if (string.IsNullOrWhiteSpace(refundTransfer.Id))
            {
                throw new FinixGatewayException("Finix returned a refund without a transfer id.", HttpStatusCode.BadGateway);
            }

            var actualAmountCents = refundTransfer.Amount ?? amountCents;
            var paymentRefund = new PaymentRefund(
                order.Id,
                refundTransfer.Id!,
                order.FinixCaptureTransferId!,
                actualAmountCents,
                refundTransfer.Currency?.Value ?? order.PaymentCurrency ?? _gateway.CurrencyCode,
                request.IdempotencyKey,
                refundTransfer.State?.Value);

            _db.PaymentRefunds.Add(paymentRefund);
            order.RecordRefund(actualAmountCents);
            await _db.SaveChangesAsync(cancellationToken);

            refunds.Add(paymentRefund);
            return MapRefundResponse(order, paymentRefund, refunds);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentMethodDto> CreatePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        ValidateCard(request.Card);

        var gate = LockFor($"buyer:{buyerId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
            var paymentInstrument = await _gateway.CreateCardPaymentInstrumentAsync(
                identityId,
                MapCard(request.Card),
                cancellationToken);

            var saved = new SavedPaymentMethod(
                buyerId,
                paymentInstrument.Id!,
                paymentInstrument.Brand?.Value,
                paymentInstrument.LastFour,
                paymentInstrument.ExpirationMonth,
                paymentInstrument.ExpirationYear,
                paymentInstrument.Name ?? request.Card.Name);

            _db.SavedPaymentMethods.Add(saved);
            await _db.SaveChangesAsync(cancellationToken);

            return MapPaymentMethod(saved);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var saved = await _db.SavedPaymentMethods
            .Where(paymentMethod => paymentMethod.BuyerId == buyerId)
            .OrderByDescending(paymentMethod => paymentMethod.CreatedAt)
            .ToListAsync(cancellationToken);

        return saved.Select(MapPaymentMethod).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var gate = LockFor($"buyer:{buyerId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
                paymentMethod => paymentMethod.Id == paymentMethodId && paymentMethod.BuyerId == buyerId,
                cancellationToken);

            if (saved is null)
            {
                throw new PaymentNotFoundException($"Saved payment method {paymentMethodId} was not found.");
            }

            try
            {
                await _gateway.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            }
            catch (FinixGatewayException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
            }

            _db.SavedPaymentMethods.Remove(saved);
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ReconciliationResponse> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentValidationException("The reconciliation 'from' value must be before 'to'.");
        }

        var authorizations = await _gateway.ListAuthorizationsAsync(from, to, cancellationToken);
        var transfers = await _gateway.ListTransfersAsync(from, to, cancellationToken);

        var orders = await _db.Orders.AsNoTracking().ToListAsync(cancellationToken);
        var refunds = await _db.PaymentRefunds.AsNoTracking().ToListAsync(cancellationToken);

        var ordersByAuthorization = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            .GroupBy(order => order.FinixAuthorizationId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var ordersByCapture = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.FinixCaptureTransferId))
            .GroupBy(order => order.FinixCaptureTransferId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var refundsByTransfer = refunds
            .Where(refund => !string.IsNullOrWhiteSpace(refund.FinixTransferId))
            .GroupBy(refund => refund.FinixTransferId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var entries = new List<ReconciliationEntryDto>();
        var finixAuthorizationIds = new HashSet<string>(StringComparer.Ordinal);
        var finixTransferIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var authorization in authorizations)
        {
            if (string.IsNullOrWhiteSpace(authorization.Id))
            {
                continue;
            }

            finixAuthorizationIds.Add(authorization.Id!);
            ordersByAuthorization.TryGetValue(authorization.Id!, out var order);
            entries.Add(new ReconciliationEntryDto
            {
                Source = "Finix",
                Kind = "AUTHORIZATION",
                Id = authorization.Id,
                Type = "AUTHORIZATION",
                State = authorization.State?.Value,
                AmountCents = authorization.Amount,
                Amount = FromCents(authorization.Amount),
                Currency = authorization.Currency?.Value,
                CreatedAt = authorization.CreatedAt,
                OrderId = order?.Id,
                MatchStatus = order is null ? "FinixOnly" : "Matched",
                Detail = authorization.IsVoid == true ? "Authorization is void." : null
            });
        }

        foreach (var transfer in transfers)
        {
            if (string.IsNullOrWhiteSpace(transfer.Id))
            {
                continue;
            }

            finixTransferIds.Add(transfer.Id!);

            int? orderId = null;
            string? refundId = null;
            string kind;
            if (string.Equals(transfer.Type?.Value, "REVERSAL", StringComparison.Ordinal))
            {
                kind = "REFUND";
                if (refundsByTransfer.TryGetValue(transfer.Id!, out var refund))
                {
                    orderId = refund.OrderId;
                    refundId = refund.FinixTransferId;
                }
            }
            else
            {
                kind = "TRANSFER";
                if (ordersByCapture.TryGetValue(transfer.Id!, out var order))
                {
                    orderId = order.Id;
                }
            }

            entries.Add(new ReconciliationEntryDto
            {
                Source = "Finix",
                Kind = kind,
                Id = transfer.Id,
                ParentId = transfer.ParentTransfer,
                Type = transfer.Type?.Value,
                State = transfer.State?.Value,
                AmountCents = transfer.Amount,
                Amount = FromCents(transfer.Amount),
                Currency = transfer.Currency?.Value,
                CreatedAt = transfer.CreatedAt,
                OrderId = orderId,
                RefundId = refundId,
                MatchStatus = orderId is null ? "FinixOnly" : "Matched"
            });
        }

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId)
                && !finixAuthorizationIds.Contains(order.FinixAuthorizationId)
                && InRange(order.OrderDate, from, to))
            {
                entries.Add(new ReconciliationEntryDto
                {
                    Source = "eShop",
                    Kind = "AUTHORIZATION",
                    Id = order.FinixAuthorizationId,
                    Type = "AUTHORIZATION",
                    State = order.FinixAuthorizationState,
                    AmountCents = order.AuthorizedAmountCents,
                    Amount = FromCents(order.AuthorizedAmountCents),
                    Currency = order.PaymentCurrency,
                    CreatedAt = order.OrderDate,
                    OrderId = order.Id,
                    MatchStatus = "EShopOnly",
                    Detail = "eShop has this authorization but Finix did not return it for the range."
                });
            }

            if (!string.IsNullOrWhiteSpace(order.FinixCaptureTransferId)
                && !finixTransferIds.Contains(order.FinixCaptureTransferId)
                && InRange(order.FulfilledAt ?? order.OrderDate, from, to))
            {
                entries.Add(new ReconciliationEntryDto
                {
                    Source = "eShop",
                    Kind = "CAPTURE",
                    Id = order.FinixCaptureTransferId,
                    Type = "DEBIT",
                    State = order.FinixCaptureState,
                    AmountCents = order.CapturedAmountCents,
                    Amount = FromCents(order.CapturedAmountCents),
                    Currency = order.PaymentCurrency,
                    CreatedAt = order.FulfilledAt ?? order.OrderDate,
                    OrderId = order.Id,
                    MatchStatus = "EShopOnly",
                    Detail = "eShop has this capture but Finix did not return it for the range."
                });
            }
        }

        foreach (var refund in refunds)
        {
            if (!finixTransferIds.Contains(refund.FinixTransferId) && InRange(refund.CreatedAt, from, to))
            {
                entries.Add(new ReconciliationEntryDto
                {
                    Source = "eShop",
                    Kind = "REFUND",
                    Id = refund.FinixTransferId,
                    ParentId = refund.FinixParentTransferId,
                    Type = "REVERSAL",
                    State = refund.State,
                    AmountCents = refund.AmountCents,
                    Amount = FromCents(refund.AmountCents),
                    Currency = refund.Currency,
                    CreatedAt = refund.CreatedAt,
                    OrderId = refund.OrderId,
                    RefundId = refund.FinixTransferId,
                    MatchStatus = "EShopOnly",
                    Detail = "eShop has this refund but Finix did not return it for the range."
                });
            }
        }

        entries = entries
            .OrderBy(entry => entry.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToList();

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            Entries = entries,
            Unmatched = entries.Where(entry => !string.Equals(entry.MatchStatus, "Matched", StringComparison.Ordinal)).ToList()
        };

        response.Summary = new ReconciliationSummary
        {
            FinixTransactionCount = authorizations.Count + transfers.Count,
            MatchedCount = entries.Count(entry => string.Equals(entry.MatchStatus, "Matched", StringComparison.Ordinal)),
            FinixOnlyCount = entries.Count(entry => string.Equals(entry.MatchStatus, "FinixOnly", StringComparison.Ordinal)),
            EShopOnlyCount = entries.Count(entry => string.Equals(entry.MatchStatus, "EShopOnly", StringComparison.Ordinal))
        };

        return response;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(entity => entity.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
            .FirstOrDefaultAsync(entity => entity.Id == orderId, cancellationToken);

        return order ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<string> GetOrCreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _db.PaymentCustomers
            .FirstOrDefaultAsync(customer => customer.BuyerId == buyerId, cancellationToken);

        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        await IdentityLock.WaitAsync(cancellationToken);
        try
        {
            existing = await _db.PaymentCustomers
                .FirstOrDefaultAsync(customer => customer.BuyerId == buyerId, cancellationToken);

            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            var identity = await _gateway.CreateBuyerIdentityAsync(buyerId, cancellationToken);
            if (string.IsNullOrWhiteSpace(identity.Id))
            {
                throw new FinixGatewayException("Finix returned an identity without an id.", HttpStatusCode.BadGateway);
            }

            var customer = new PaymentCustomer(buyerId, identity.Id!);
            _db.PaymentCustomers.Add(customer);
            await _db.SaveChangesAsync(cancellationToken);
            return customer.FinixIdentityId;
        }
        finally
        {
            IdentityLock.Release();
        }
    }

    private async Task AdoptCaptureAsync(Order order, string transferId, CancellationToken cancellationToken)
    {
        var transfer = await LoadCaptureTransferAsync(transferId, cancellationToken);
        var capturedCents = transfer.Amount ?? order.AuthorizedAmountCents ?? ToCents(order.Total());
        var feeCents = transfer.Fee ?? 0;
        var netCents = Math.Max(0, capturedCents - feeCents);

        order.MarkCaptured(
            transferId,
            transfer.State?.Value ?? "UNKNOWN",
            capturedCents,
            feeCents,
            netCents,
            transfer.Currency?.Value ?? order.PaymentCurrency ?? _gateway.CurrencyCode);
    }

    private async Task<bool> EnsureCaptureSucceededAsync(Order order, CancellationToken cancellationToken)
    {
        if (string.Equals(order.FinixCaptureState, "SUCCEEDED", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(order.FinixCaptureTransferId))
        {
            return false;
        }

        for (var attempt = 0; attempt < 75; attempt++)
        {
            var transfer = await _gateway.GetTransferAsync(order.FinixCaptureTransferId!, cancellationToken);
            if (string.Equals(transfer.State?.Value, "SUCCEEDED", StringComparison.Ordinal))
            {
                await AdoptCaptureAsync(order, order.FinixCaptureTransferId!, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }

            if (attempt < 74)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        return false;
    }

    private async Task<FinixApiReference.Models.Transfer> LoadCaptureTransferAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        FinixApiReference.Models.Transfer? transfer = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            transfer = await _gateway.GetTransferAsync(transferId, cancellationToken);
            if (transfer.Fee is not null
                && string.Equals(transfer.State?.Value, "SUCCEEDED", StringComparison.Ordinal))
            {
                break;
            }

            if (attempt < 2)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        return transfer ?? throw new FinixGatewayException(
            "Finix did not return the capture transfer.",
            HttpStatusCode.BadGateway);
    }

    private async Task<FinixApiReference.Models.Authorization?> TryGetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (FinixGatewayException)
        {
            return null;
        }
    }

    private static string ResolveVoidState(FinixApiReference.Models.Authorization authorization)
    {
        if (authorization.IsVoid == true)
        {
            return "VOIDED";
        }

        return authorization.VoidState?.Value ?? authorization.State?.Value ?? "UNKNOWN";
    }

    private static void ValidateCard(CardPaymentRequest card)
    {
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentValidationException("The card number is not valid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("The card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear is < 2000 or > 2100)
        {
            throw new PaymentValidationException("The card expiration year is not valid.");
        }

        var now = DateTimeOffset.UtcNow;
        if (card.ExpirationYear < now.Year
            || (card.ExpirationYear == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentValidationException("The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("The cardholder name is required.");
        }

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            var securityCode = new string(card.SecurityCode.Where(char.IsDigit).ToArray());
            if (securityCode.Length is < 3 or > 4)
            {
                throw new PaymentValidationException("The card security code is not valid.");
            }
        }
    }

    private static FinixCardDetails MapCard(CardPaymentRequest card) =>
        new()
        {
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new FinixBillingAddress
                {
                    Line1 = card.BillingAddress.Line1,
                    City = card.BillingAddress.City,
                    Region = card.BillingAddress.Region,
                    PostalCode = card.BillingAddress.PostalCode,
                    Country = card.BillingAddress.Country
                }
        };

    private static Address MapAddress(ShippingAddressDto? address) =>
        address is null
            ? new Address("1 eShop Way", "Redmond", "WA", "USA", "98052")
            : new Address(
                string.IsNullOrWhiteSpace(address.Street) ? "1 eShop Way" : address.Street,
                string.IsNullOrWhiteSpace(address.City) ? "Redmond" : address.City,
                string.IsNullOrWhiteSpace(address.State) ? "WA" : address.State,
                string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country,
                string.IsNullOrWhiteSpace(address.ZipCode) ? "98052" : address.ZipCode);

    private static OrderDto MapOrder(Order order, IEnumerable<PaymentRefund> refunds)
    {
        var totalCents = ToCents(order.Total());
        var refundList = refunds
            .OrderBy(refund => refund.CreatedAt)
            .ToList();
        var refundedCents = refundList.Sum(refund => refund.AmountCents);
        var capturedCents = order.CapturedAmountCents ?? 0;
        var refundableCents = Math.Max(0, capturedCents - refundedCents);

        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            TotalCents = totalCents,
            Currency = order.PaymentCurrency,
            FinixAuthorizationId = order.FinixAuthorizationId,
            FinixAuthorizationState = order.FinixAuthorizationState,
            AuthorizedAmountCents = order.AuthorizedAmountCents,
            FinixPaymentInstrumentId = order.FinixPaymentInstrumentId,
            FinixCaptureTransferId = order.FinixCaptureTransferId,
            FinixCaptureState = order.FinixCaptureState,
            CapturedAmountCents = order.CapturedAmountCents,
            CapturedAmount = FromCents(order.CapturedAmountCents),
            FinixFeeCents = order.FinixFeeCents,
            FinixFee = FromCents(order.FinixFeeCents),
            NetProceedsCents = order.NetProceedsCents,
            NetProceeds = FromCents(order.NetProceedsCents),
            RefundedAmountCents = refundedCents,
            RefundedAmount = FromCents(refundedCents),
            RefundableAmountCents = refundableCents,
            RefundableAmount = FromCents(refundableCents),
            FulfilledAt = order.FulfilledAt,
            CanceledAt = order.CanceledAt,
            Items = order.OrderItems
                .Select(item => new OrderItemDto
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    UnitPrice = item.UnitPrice,
                    Units = item.Units,
                    LineTotal = item.UnitPrice * item.Units
                })
                .ToList(),
            Refunds = refundList
                .Select(refund => new RefundDto
                {
                    RefundId = refund.FinixTransferId,
                    PaymentRefundId = refund.Id,
                    AmountCents = refund.AmountCents,
                    Amount = FromCents(refund.AmountCents),
                    Currency = refund.Currency,
                    State = refund.State,
                    CreatedAt = refund.CreatedAt
                })
                .ToList()
        };
    }

    private static PayOrderResponse MapPayResponse(Order order) =>
        new()
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            AuthorizationId = order.FinixAuthorizationId,
            AuthorizationState = order.FinixAuthorizationState,
            AuthorizedAmountCents = order.AuthorizedAmountCents,
            Currency = order.PaymentCurrency,
            PaymentInstrumentId = order.FinixPaymentInstrumentId,
            Order = MapOrder(order, Array.Empty<PaymentRefund>())
        };

    private static FulfilOrderResponse MapFulfilResponse(Order order) =>
        new()
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            CaptureTransferId = order.FinixCaptureTransferId,
            CaptureState = order.FinixCaptureState,
            CapturedAmountCents = order.CapturedAmountCents,
            FeeCents = order.FinixFeeCents,
            NetProceedsCents = order.NetProceedsCents,
            Currency = order.PaymentCurrency,
            Order = MapOrder(order, Array.Empty<PaymentRefund>())
        };

    private static CancelOrderResponse MapCancelResponse(Order order) =>
        new()
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            AuthorizationState = order.FinixAuthorizationState,
            Order = MapOrder(order, Array.Empty<PaymentRefund>())
        };

    private static RefundResponse MapRefundResponse(Order order, PaymentRefund refund, IReadOnlyCollection<PaymentRefund> refunds)
    {
        var refundedCents = refunds.Sum(item => item.AmountCents);
        var capturedCents = order.CapturedAmountCents ?? 0;
        return new RefundResponse
        {
            RefundId = refund.FinixTransferId,
            PaymentRefundId = refund.Id,
            OrderId = order.Id,
            AmountCents = refund.AmountCents,
            Amount = FromCents(refund.AmountCents),
            Currency = refund.Currency,
            State = refund.State,
            RefundedAmountCents = refundedCents,
            RefundableAmountCents = Math.Max(0, capturedCents - refundedCents),
            PaymentStatus = order.PaymentStatus.ToString()
        };
    }

    private static PaymentMethodDto MapPaymentMethod(SavedPaymentMethod saved) =>
        new()
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            Last4 = saved.LastFour,
            ExpirationMonth = saved.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear,
            CardholderName = saved.CardholderName,
            FinixPaymentInstrumentId = saved.FinixPaymentInstrumentId,
            CreatedAt = saved.CreatedAt
        };

    private static SemaphoreSlim LockFor(string key) =>
        Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    private static long ToCents(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private static decimal? FromCents(long? cents) =>
        cents is null ? null : cents.Value / 100m;

    private static decimal FromCents(long cents) => cents / 100m;

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;
}


