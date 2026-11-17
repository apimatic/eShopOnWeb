using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly CatalogContext _db;
    private readonly IFinixPaymentGateway _finix;
    private readonly FinixOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(CatalogContext db, IFinixPaymentGateway finix, IOptions<FinixOptions> options, ILogger<PaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string ownerId, CreateOrderRequest request, CancellationToken ct)
    {
        var lines = request.ResolveItems();
        if (lines is null || lines.Count == 0)
        {
            throw PaymentException.BadRequest("At least one order item is required.");
        }

        foreach (var line in lines)
        {
            if (line.ResolveCatalogItemId() <= 0 || line.Quantity <= 0)
            {
                throw PaymentException.BadRequest("Every order item needs a valid catalogItemId and a quantity greater than zero.");
            }
        }

        var catalogIds = lines.Select(i => i.ResolveCatalogItemId()).Distinct().ToArray();
        var catalogItems = (await _db.CatalogItems.Where(i => catalogIds.Contains(i.Id)).ToListAsync(ct))
            .ToDictionary(i => i.Id);

        if (catalogItems.Count != catalogIds.Length)
        {
            throw PaymentException.BadRequest("One or more catalog items could not be found.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems[line.ResolveCatalogItemId()];
            var picture = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, line.Quantity));
        }

        var address = BuildShippingAddress(request.ShippingAddress ?? request.Address);
        var order = new Order(ownerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var amountCents = ToCents(order.Total());
        var payment = new OrderPayment(order.Id, ownerId, _options.ResolveCurrency(), amountCents);
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(ct);

        return new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = payment.Status.ToString(),
            Total = order.Total(),
            AmountCents = amountCents,
            Currency = payment.Currency,
            Items = MapOrderItems(order)
        };
    }

    public async Task<PaymentResponse> PayAsync(string ownerId, PayOrderRequest request, CancellationToken ct)
    {
        var gate = GetOrderLock(request.OrderId);
        await gate.WaitAsync(ct);
        OrderPayment? paymentForError = null;
        try
        {
            var order = await _db.Orders.Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.BuyerId == ownerId, ct);
            if (order is null)
            {
                throw PaymentException.NotFound("Order not found.");
            }

            var payment = await GetPaymentAsync(order.Id, ct);
            if (payment is null)
            {
                throw PaymentException.Conflict("This order does not have a payment record.");
            }
            paymentForError = payment;

            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return ToPaymentResponse(payment, idempotent: true);
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                throw PaymentException.Conflict("This order was canceled. Create a new order to take payment.");
            }

            var savedMethodId = request.PaymentMethodId ?? request.SavedCardId ?? request.SavedPaymentMethodId ?? request.CardId;
            string paymentInstrumentId;
            if (savedMethodId.HasValue)
            {
                var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
                    m => m.Id == savedMethodId.Value && m.OwnerId == ownerId && !m.IsDeleted, ct);
                if (saved is null)
                {
                    throw PaymentException.NotFound("Saved payment method not found.");
                }
                paymentInstrumentId = saved.FinixPaymentInstrumentId;
            }
            else if (!string.IsNullOrWhiteSpace(request.PaymentInstrumentId))
            {
                var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
                    m => m.FinixPaymentInstrumentId == request.PaymentInstrumentId && m.OwnerId == ownerId && !m.IsDeleted, ct);
                if (saved is null)
                {
                    throw PaymentException.NotFound("Saved payment method not found.");
                }
                paymentInstrumentId = saved.FinixPaymentInstrumentId;
            }
            else
            {
                var card = ResolveCard(request.Card, request.Number, request.ExpirationMonth, request.ExpirationYear,
                    request.SecurityCode, request.Cvc, request.Cvv, request.Name, request.Address);
                var customer = await GetOrCreateCustomerAsync(ownerId, card, ct);
                var instrument = await _finix.CreatePaymentInstrumentAsync(new FinixCreatePaymentInstrumentRequest
                {
                    IdentityId = customer.FinixIdentityId,
                    Number = card.Number,
                    ExpirationMonth = card.ExpirationMonth,
                    ExpirationYear = card.ExpirationYear,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    Address = card.Address,
                    Tags = new Dictionary<string, string>
                    {
                        ["ownerId"] = ownerId,
                        ["orderId"] = order.Id.ToString(),
                        ["paymentReference"] = payment.PaymentReference
                    }
                }, ct);
                paymentInstrumentId = instrument.Id;
            }

            if (payment.Status == PaymentStatus.Failed
                || string.IsNullOrWhiteSpace(payment.AuthorizationIdempotencyId)
                || (!string.IsNullOrWhiteSpace(payment.PaymentInstrumentId) && payment.PaymentInstrumentId != paymentInstrumentId))
            {
                payment.BeginAuthorization(paymentInstrumentId, $"eshop-{Guid.NewGuid():N}");
                await _db.SaveChangesAsync(ct);
            }

            var authorization = await _finix.AuthorizeAsync(new FinixAuthorizationRequest
            {
                AmountCents = payment.AmountCents,
                Currency = payment.Currency,
                MerchantId = _options.MerchantId ?? string.Empty,
                Source = paymentInstrumentId,
                IdempotencyId = payment.AuthorizationIdempotencyId!,
                Tags = new Dictionary<string, string>
                {
                    ["ownerId"] = ownerId,
                    ["orderId"] = order.Id.ToString(),
                    ["paymentReference"] = payment.PaymentReference
                }
            }, ct);

            if (string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorizationFailed(authorization.State);
                await _db.SaveChangesAsync(ct);
                throw PaymentException.PaymentRequired(
                    $"Finix declined the authorization ({authorization.FailureCode ?? authorization.State}): {authorization.FailureMessage ?? "no further detail was returned"}.");
            }

            if (authorization.AmountCents != payment.AmountCents)
            {
                await TryVoidAsync(authorization.Id, ct);
                payment.MarkAuthorizationFailed(authorization.State);
                await _db.SaveChangesAsync(ct);
                throw PaymentException.Upstream(
                    $"Finix authorized {authorization.AmountCents} cents but the order total is {payment.AmountCents} cents. The authorization was released.");
            }

            payment.MarkAuthorized(paymentInstrumentId, authorization.Id, authorization.State,
                authorization.AmountCents, authorization.ExpiresAt);
            await _db.SaveChangesAsync(ct);

            return ToPaymentResponse(payment, idempotent: false);
        }
        catch (FinixApiException ex)
        {
            if (paymentForError is not null
                && paymentForError.Status == PaymentStatus.AwaitingPayment
                && string.IsNullOrWhiteSpace(paymentForError.AuthorizationId))
            {
                paymentForError.MarkAuthorizationFailed("ERROR");
                await _db.SaveChangesAsync(CancellationToken.None);
            }

            throw PaymentException.Upstream($"Finix authorization failed: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken ct)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var payment = await GetPaymentAsync(orderId, ct);
            if (payment is null)
            {
                throw PaymentException.NotFound("Order payment not found.");
            }

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return ToFulfilResponse(payment, idempotent: true);
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                throw PaymentException.Conflict("This order was canceled, so there are no held funds to capture.");
            }

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                throw PaymentException.Conflict("This order has not been authorized. The shopper must pay for the order before it can be fulfilled.");
            }

            var authorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId, ct);
            var uncapturableReason = GetUncapturableReason(authorization);
            if (uncapturableReason is not null)
            {
                throw PaymentException.Conflict(
                    $"The authorization {payment.AuthorizationId} can no longer be captured ({uncapturableReason}). " +
                    "The order remains unfulfilled; ask the shopper to pay again so a new hold is created before retrying fulfilment.");
            }

            var captured = await _finix.CaptureAuthorizationAsync(payment.AuthorizationId, payment.AmountCents, ct);
            var captureTransferId = captured.TransferId;
            if (string.IsNullOrWhiteSpace(captureTransferId))
            {
                var refreshed = await _finix.GetAuthorizationAsync(payment.AuthorizationId, ct);
                captureTransferId = refreshed.TransferId;
            }

            if (string.IsNullOrWhiteSpace(captureTransferId))
            {
                throw PaymentException.Upstream("Finix captured the authorization but did not return a capture transfer id. The order remains unfulfilled; check Finix before retrying.");
            }

            var transfer = await WaitForTransferAsync(captureTransferId!, TimeSpan.FromSeconds(5), ct);
            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw PaymentException.Conflict(
                    $"Finix could not capture the authorization ({transfer.FailureCode ?? "FAILED"}): {transfer.FailureMessage ?? "no further detail was returned"}. " +
                    "The order remains unfulfilled; re-authorize the order before retrying.");
            }

            var capturedAmountCents = transfer.AmountCents > 0 ? transfer.AmountCents : payment.AmountCents;
            var feeCents = transfer.FeeCents ?? 0;
            var netCents = capturedAmountCents - feeCents;
            if (netCents < 0)
            {
                netCents = 0;
            }

            payment.MarkCaptured(captureTransferId!, transfer.State ?? captured.State, capturedAmountCents, feeCents, netCents);
            await _db.SaveChangesAsync(ct);

            await TryUpdateTransferTagsAsync(captureTransferId!, orderId, payment.PaymentReference, "CAPTURE", ct);

            return ToFulfilResponse(payment, idempotent: false);
        }
        catch (FinixApiException ex)
        {
            throw PaymentException.Conflict($"Finix could not capture the payment: {ex.Message}. The order remains unfulfilled; re-authorize it before retrying.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken ct)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var payment = await GetPaymentAsync(orderId, ct);
            if (payment is null)
            {
                throw PaymentException.NotFound("Order payment not found.");
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                return ToCancelResponse(payment, idempotent: true);
            }

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw PaymentException.Conflict("This order has already been fulfilled. Use the refunds endpoint to return captured money.");
            }

            if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                var voided = await _finix.VoidAuthorizationAsync(payment.AuthorizationId, ct);
                if (string.Equals(voided.VoidState, "FAILED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(voided.State, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    throw PaymentException.Conflict(
                        $"Finix could not release the held funds ({voided.FailureCode ?? "VOID_FAILED"}): {voided.FailureMessage ?? "no further detail was returned"}. " +
                        "Retry the cancel operation.");
                }

                payment.MarkCanceled(voided.VoidState);
                await _db.SaveChangesAsync(ct);
                return ToCancelResponse(payment, idempotent: false);
            }

            payment.MarkCanceled(null);
            await _db.SaveChangesAsync(ct);
            return ToCancelResponse(payment, idempotent: false);
        }
        catch (FinixApiException ex)
        {
            throw PaymentException.Conflict($"Finix could not release the held funds: {ex.Message}. No money was captured; retry the cancel operation.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundOrderResponse> RefundAsync(string ownerId, bool isAdministrator, RefundOrderRequest request, CancellationToken ct)
    {
        var gate = GetOrderLock(request.OrderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = isAdministrator
                ? await _db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId, ct)
                : await _db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId && o.BuyerId == ownerId, ct);
            if (order is null)
            {
                throw PaymentException.NotFound("Order not found.");
            }

            var payment = await GetPaymentAsync(order.Id, ct);
            if (payment is null)
            {
                throw PaymentException.NotFound("Order payment not found.");
            }

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
            {
                throw PaymentException.Conflict("Only captured payments can be refunded. This order has not been fulfilled.");
            }

            var idempotencyKey = (request.IdempotencyKey ?? request.IdempotencyId)?.Trim();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw PaymentException.BadRequest("A non-empty idempotencyKey is required for refunds.");
            }

            var existing = payment.Refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
            if (existing is not null)
            {
                return ToRefundResponse(payment, existing, idempotent: true);
            }

            var remaining = payment.RefundableAmountCents;
            var amountCents = request.AmountCents
                ?? request.RefundAmountCents
                ?? (request.Amount.HasValue ? ToCents(request.Amount.Value) : remaining);

            if (amountCents <= 0)
            {
                throw PaymentException.BadRequest("The refund amount must be greater than zero.");
            }

            if (amountCents > remaining)
            {
                throw PaymentException.Conflict(
                    $"The refund amount ({amountCents} cents) exceeds the remaining refundable amount ({remaining} cents).");
            }

            if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                throw PaymentException.Conflict("The captured Finix transfer id is missing, so the payment cannot be refunded.");
            }

            if (amountCents < (payment.CapturedAmountCents ?? 0)
                && !string.Equals(payment.CaptureState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                // Finix only accepts partial reversals once the capture transfer has settled.
                var captureTransfer = await WaitForTransferAsync(payment.CaptureTransferId!, TimeSpan.FromSeconds(90), ct);
                if (!string.Equals(captureTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    throw PaymentException.Conflict(
                        $"Finix is still settling the capture transfer ({captureTransfer.State ?? "UNKNOWN"}). " +
                        "Retry the partial refund shortly, or refund the full captured amount.");
                }
            }

            var refundTransfer = await _finix.RefundTransferAsync(payment.CaptureTransferId!, amountCents, ct);
            if (string.Equals(refundTransfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(refundTransfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                throw PaymentException.Conflict(
                    $"Finix declined the refund ({refundTransfer.FailureCode ?? refundTransfer.State}): {refundTransfer.FailureMessage ?? "no further detail was returned"}.");
            }

            var localRefund = payment.AddRefund(idempotencyKey!, amountCents, refundTransfer.Id, refundTransfer.State);
            await _db.SaveChangesAsync(ct);

            await TryUpdateTransferTagsAsync(refundTransfer.Id, order.Id, payment.PaymentReference, "REFUND", ct);

            return ToRefundResponse(payment, localRefund, idempotent: false);
        }
        catch (FinixApiException ex)
        {
            throw PaymentException.Conflict($"Finix could not refund the payment: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string ownerId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Where(o => o.BuyerId == ownerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments.AsNoTracking()
            .Include(p => p.Refunds)
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync(ct);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        var response = new MyOrdersResponse();
        foreach (var order in orders)
        {
            paymentByOrder.TryGetValue(order.Id, out var payment);
            var dto = new MyOrderDto
            {
                OrderId = order.Id,
                Id = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                AmountCents = payment?.AmountCents ?? ToCents(order.Total()),
                Currency = payment?.Currency ?? _options.ResolveCurrency(),
                PaymentStatus = payment?.Status.ToString() ?? "Unpaid",
                AuthorizationId = payment?.AuthorizationId,
                AuthorizationState = payment?.AuthorizationState,
                CaptureTransferId = payment?.CaptureTransferId,
                CaptureState = payment?.CaptureState,
                CapturedAmountCents = payment?.CapturedAmountCents,
                CapturedAmount = payment?.CapturedAmountCents.HasValue == true ? ToMoney(payment.CapturedAmountCents!.Value) : null,
                FeeCents = payment?.FinixFeeCents,
                Fee = payment?.FinixFeeCents.HasValue == true ? ToMoney(payment.FinixFeeCents!.Value) : null,
                NetCents = payment?.NetAmountCents,
                Net = payment?.NetAmountCents.HasValue == true ? ToMoney(payment.NetAmountCents!.Value) : null,
                RefundedAmountCents = payment?.RefundedAmountCents ?? 0,
                RefundedAmount = ToMoney(payment?.RefundedAmountCents ?? 0),
                Items = MapOrderItems(order)
            };

            if (payment is not null)
            {
                dto.Refunds = payment.Refunds.Select(MapRefund).ToList();
            }

            response.Orders.Add(dto);
        }

        return response;
    }

    public async Task<PaymentMethodDto> SavePaymentMethodAsync(string ownerId, SavePaymentMethodRequest request, CancellationToken ct)
    {
        var card = ResolveCard(request.Card, request.Number, request.ExpirationMonth, request.ExpirationYear,
            request.SecurityCode, request.Cvc, request.Cvv, request.Name, request.Address);

        var customerGate = GetCustomerLock(ownerId);
        await customerGate.WaitAsync(ct);
        try
        {
            var customer = await GetOrCreateCustomerAsync(ownerId, card, ct);
            var instrument = await _finix.CreatePaymentInstrumentAsync(new FinixCreatePaymentInstrumentRequest
            {
                IdentityId = customer.FinixIdentityId,
                Number = card.Number,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                Address = card.Address,
                Tags = new Dictionary<string, string> { ["ownerId"] = ownerId }
            }, ct);

            var saved = new SavedPaymentMethod(ownerId, instrument.Id,
                instrument.Brand ?? "UNKNOWN",
                instrument.Last4 ?? card.Number[^4..],
                instrument.ExpirationMonth ?? card.ExpirationMonth,
                instrument.ExpirationYear ?? card.ExpirationYear);

            _db.SavedPaymentMethods.Add(saved);
            await _db.SaveChangesAsync(ct);
            return MapPaymentMethod(saved);
        }
        catch (FinixApiException ex)
        {
            throw PaymentException.Upstream($"Finix could not save the card: {ex.Message}");
        }
        finally
        {
            customerGate.Release();
        }
    }

    public async Task<PaymentMethodsResponse> GetPaymentMethodsAsync(string ownerId, CancellationToken ct)
    {
        var cards = await _db.SavedPaymentMethods.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && !m.IsDeleted)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        return new PaymentMethodsResponse { PaymentMethods = cards.Select(MapPaymentMethod).ToList() };
    }

    public async Task<DeletePaymentMethodResponse> DeletePaymentMethodAsync(string ownerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
            m => m.Id == paymentMethodId && m.OwnerId == ownerId && !m.IsDeleted, ct);
        if (saved is null)
        {
            throw PaymentException.NotFound("Saved payment method not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, ct);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Finix could not disable saved payment method {PaymentMethodId}: {Message}", saved.Id, ex.Message);
        }

        saved.MarkDeleted();
        await _db.SaveChangesAsync(ct);

        return new DeletePaymentMethodResponse { PaymentMethodId = saved.Id, Deleted = true };
    }

    public async Task<ReconciliationResponse> ReconcileAsync(ReconciliationRequest request, CancellationToken ct)
    {
        if (request.From is null || request.To is null)
        {
            throw PaymentException.BadRequest("Both from and to ISO-8601 date-times are required.");
        }

        var from = request.From.Value;
        var to = request.To.Value;
        if (from > to)
        {
            throw PaymentException.BadRequest("The from date-time must be before or equal to the to date-time.");
        }

        var authorizations = await _finix.ListAuthorizationsAsync(from, to, ct);
        var transfers = await _finix.ListTransfersAsync(from, to, ct);

        var allOrderIds = await _db.Orders.AsNoTracking().Select(o => o.Id).ToListAsync(ct);
        var knownOrderIds = allOrderIds.ToHashSet();

        var localOrders = await _db.Orders.AsNoTracking()
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .OrderBy(o => o.OrderDate)
            .ToListAsync(ct);

        var localOrderIds = localOrders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments.AsNoTracking()
            .Include(p => p.Refunds)
            .Where(p => localOrderIds.Contains(p.OrderId)
                || (p.AuthorizedAt >= from && p.AuthorizedAt <= to)
                || (p.CapturedAt >= from && p.CapturedAt <= to)
                || p.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .ToListAsync(ct);

        var authorizationToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var transferToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var referenceToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var paymentByOrder = new Dictionary<int, OrderPayment>();

        foreach (var payment in payments)
        {
            paymentByOrder[payment.OrderId] = payment;
            if (!string.IsNullOrWhiteSpace(payment.PaymentReference))
            {
                referenceToOrder[payment.PaymentReference] = payment.OrderId;
            }
            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                authorizationToOrder[payment.AuthorizationId!] = payment.OrderId;
            }
            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                transferToOrder[payment.CaptureTransferId!] = payment.OrderId;
            }
            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrWhiteSpace(refund.FinixTransferId))
                {
                    transferToOrder[refund.FinixTransferId!] = payment.OrderId;
                }
            }
        }

        var response = new ReconciliationResponse { From = from, To = to };
        var matchedOrderIds = new HashSet<int>();

        foreach (var authorization in authorizations)
        {
            var orderId = GetOrderIdFromPaymentReference(authorization.Tags, referenceToOrder)
                ?? (authorizationToOrder.TryGetValue(authorization.Id, out var byId) ? (int?)byId : null);
            var matched = orderId.HasValue && knownOrderIds.Contains(orderId.Value);
            if (matched)
            {
                matchedOrderIds.Add(orderId!.Value);
            }

            response.FinixTransactions.Add(new ReconciliationTransactionDto
            {
                FinixId = authorization.Id,
                Kind = "AUTHORIZATION",
                Type = "AUTHORIZATION",
                AmountCents = authorization.AmountCents,
                Amount = ToMoney(authorization.AmountCents),
                Currency = authorization.Currency ?? _options.ResolveCurrency(),
                State = authorization.State ?? "UNKNOWN",
                CreatedAt = authorization.CreatedAt,
                OrderId = matched ? orderId : null,
                MatchStatus = matched ? "MATCHED" : "FINIX_ONLY"
            });
        }

        foreach (var transfer in transfers)
        {
            var orderId = GetOrderIdFromPaymentReference(transfer.Tags, referenceToOrder)
                ?? (transferToOrder.TryGetValue(transfer.Id, out var byId) ? (int?)byId : null)
                ?? (!string.IsNullOrWhiteSpace(transfer.ParentTransferId) && transferToOrder.TryGetValue(transfer.ParentTransferId!, out var byParent) ? (int?)byParent : null);

            var matched = orderId.HasValue && knownOrderIds.Contains(orderId.Value);
            if (matched)
            {
                matchedOrderIds.Add(orderId!.Value);
            }

            var kind = GetTransferKind(transfer);
            response.FinixTransactions.Add(new ReconciliationTransactionDto
            {
                FinixId = transfer.Id,
                Kind = kind,
                Type = transfer.Type ?? "UNKNOWN",
                AmountCents = transfer.AmountCents,
                Amount = ToMoney(transfer.AmountCents),
                Currency = transfer.Currency ?? _options.ResolveCurrency(),
                State = transfer.State ?? "UNKNOWN",
                CreatedAt = transfer.CreatedAt,
                OrderId = matched ? orderId : null,
                MatchStatus = matched ? "MATCHED" : "FINIX_ONLY",
                OperationKey = transfer.OperationKey,
                ParentId = transfer.ParentTransferId
            });
        }

        foreach (var order in localOrders)
        {
            paymentByOrder.TryGetValue(order.Id, out var payment);
            var matched = matchedOrderIds.Contains(order.Id);
            response.EshopOrders.Add(new ReconciliationOrderDto
            {
                OrderId = order.Id,
                Total = order.Total(),
                AmountCents = payment?.AmountCents ?? ToCents(order.Total()),
                Currency = payment?.Currency ?? _options.ResolveCurrency(),
                OrderDate = order.OrderDate,
                PaymentStatus = payment?.Status.ToString() ?? "Unpaid",
                MatchStatus = matched ? "MATCHED" : "ESHOP_ONLY"
            });
        }

        response.UnmatchedFinixTransactions = response.FinixTransactions
            .Where(t => t.MatchStatus == "FINIX_ONLY").ToList();
        response.UnmatchedEshopOrders = response.EshopOrders
            .Where(o => o.MatchStatus == "ESHOP_ONLY").ToList();

        response.Summary = new ReconciliationSummaryDto
        {
            FinixTransactionCount = response.FinixTransactions.Count,
            MatchedCount = response.FinixTransactions.Count(t => t.MatchStatus == "MATCHED"),
            FinixOnlyCount = response.UnmatchedFinixTransactions.Count,
            EshopOrderCount = response.EshopOrders.Count,
            EshopOnlyCount = response.UnmatchedEshopOrders.Count
        };

        return response;
    }

    private async Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        return await _db.OrderPayments.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
    }

    private async Task<FinixCustomer> GetOrCreateCustomerAsync(string ownerId, ResolvedCard card, CancellationToken ct)
    {
        var customer = await _db.FinixCustomers.FirstOrDefaultAsync(c => c.OwnerId == ownerId, ct);
        if (customer is not null)
        {
            return customer;
        }

        var nameParts = (card.Name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var identity = await _finix.CreateBuyerIdentityAsync(new FinixBuyerIdentityRequest
        {
            FirstName = nameParts.Length > 0 ? nameParts[0] : null,
            LastName = nameParts.Length > 1 ? string.Join(' ', nameParts.Skip(1)) : null,
            Address = card.Address,
            Tags = new Dictionary<string, string> { ["ownerId"] = ownerId }
        }, ct);

        customer = new FinixCustomer(ownerId, identity.Id);
        _db.FinixCustomers.Add(customer);
        await _db.SaveChangesAsync(ct);
        return customer;
    }

    private async Task TryVoidAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            await _finix.VoidAuthorizationAsync(authorizationId, ct);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Could not release Finix authorization {AuthorizationId}: {Message}", authorizationId, ex.Message);
        }
    }

    private async Task<FinixTransferResult> WaitForTransferAsync(string transferId, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var transfer = await _finix.GetTransferAsync(transferId, ct);

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)
            && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            transfer = await _finix.GetTransferAsync(transferId, ct);
        }

        return transfer;
    }

    private async Task TryUpdateTransferTagsAsync(string transferId, int orderId, string paymentReference, string kind, CancellationToken ct)
    {
        try
        {
            await _finix.UpdateTransferTagsAsync(transferId, new Dictionary<string, string>
            {
                ["orderId"] = orderId.ToString(),
                ["paymentReference"] = paymentReference,
                ["kind"] = kind
            }, ct);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Could not tag Finix transfer {TransferId} with order {OrderId}: {Message}", transferId, orderId, ex.Message);
        }
    }

    private static string? GetUncapturableReason(FinixAuthorizationResult authorization)
    {
        if (authorization.IsVoid)
        {
            return "the authorization was voided";
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return $"Finix reports state {authorization.State ?? "UNKNOWN"}";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"the hold expired at {authorization.ExpiresAt.Value:O}";
        }

        return null;
    }

    private static string GetTransferKind(FinixTransferResult transfer)
    {
        if (string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(transfer.ParentTransferId))
        {
            return "REFUND";
        }

        if ((transfer.OperationKey ?? string.Empty).Contains("CAPTURE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transfer.Type, "DEBIT", StringComparison.OrdinalIgnoreCase))
        {
            return "CAPTURE";
        }

        return "TRANSFER";
    }

    private static int? GetOrderIdFromPaymentReference(Dictionary<string, JsonElement>? tags, Dictionary<string, int> referenceToOrder)
    {
        if (tags is null || !tags.TryGetValue("paymentReference", out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var reference = element.GetString();
        return !string.IsNullOrWhiteSpace(reference) && referenceToOrder.TryGetValue(reference!, out var orderId)
            ? orderId
            : null;
    }

    private static OrderLineResponse MapOrderLine(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Quantity = item.Units,
        LineTotal = item.UnitPrice * item.Units
    };

    private static List<OrderLineResponse> MapOrderItems(Order order) =>
        order.OrderItems.Select(MapOrderLine).ToList();

    private static RefundDto MapRefund(PaymentRefund refund) => new()
    {
        RefundId = refund.Id,
        Id = refund.Id,
        FinixRefundId = refund.FinixTransferId,
        Amount = ToMoney(refund.AmountCents),
        AmountCents = refund.AmountCents,
        Status = refund.Status.ToString(),
        State = refund.State,
        CreatedAt = refund.CreatedAt
    };

    private static PaymentMethodDto MapPaymentMethod(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Id = saved.Id,
        Brand = saved.Brand ?? "UNKNOWN",
        Last4 = saved.Last4 ?? string.Empty,
        ExpirationMonth = saved.ExpirationMonth,
        ExpirationYear = saved.ExpirationYear,
        CreatedAt = saved.CreatedAt
    };

    private static PaymentResponse ToPaymentResponse(OrderPayment payment, bool idempotent) => new()
    {
        OrderId = payment.OrderId,
        PaymentStatus = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState,
        Amount = ToMoney(payment.AmountCents),
        AmountCents = payment.AmountCents,
        Currency = payment.Currency,
        ExpiresAt = payment.AuthorizationExpiresAt,
        Idempotent = idempotent
    };

    private static FulfilOrderResponse ToFulfilResponse(OrderPayment payment, bool idempotent) => new()
    {
        OrderId = payment.OrderId,
        PaymentStatus = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        CaptureTransferId = payment.CaptureTransferId,
        CaptureState = payment.CaptureState,
        CapturedAmountCents = payment.CapturedAmountCents ?? 0,
        CapturedAmount = ToMoney(payment.CapturedAmountCents ?? 0),
        FeeCents = payment.FinixFeeCents ?? 0,
        Fee = ToMoney(payment.FinixFeeCents ?? 0),
        NetCents = payment.NetAmountCents ?? 0,
        Net = ToMoney(payment.NetAmountCents ?? 0),
        Currency = payment.Currency,
        Idempotent = idempotent
    };

    private static CancelOrderResponse ToCancelResponse(OrderPayment payment, bool idempotent) => new()
    {
        OrderId = payment.OrderId,
        PaymentStatus = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState,
        VoidState = payment.VoidState,
        ReleasedAmountCents = payment.Status == PaymentStatus.Canceled && payment.CapturedAmountCents is null ? payment.AmountCents : 0,
        ReleasedAmount = ToMoney(payment.Status == PaymentStatus.Canceled && payment.CapturedAmountCents is null ? payment.AmountCents : 0),
        Currency = payment.Currency,
        Message = "Held funds were released; no money was captured.",
        Idempotent = idempotent
    };

    private static RefundOrderResponse ToRefundResponse(OrderPayment payment, PaymentRefund refund, bool idempotent) => new()
    {
        OrderId = payment.OrderId,
        RefundId = refund.Id,
        FinixRefundId = refund.FinixTransferId,
        AmountCents = refund.AmountCents,
        Amount = ToMoney(refund.AmountCents),
        RefundedTotalCents = payment.RefundedAmountCents,
        RefundedTotal = ToMoney(payment.RefundedAmountCents),
        RefundableRemainingCents = payment.RefundableAmountCents,
        RefundableRemaining = ToMoney(payment.RefundableAmountCents),
        CapturedAmountCents = payment.CapturedAmountCents ?? 0,
        CapturedAmount = ToMoney(payment.CapturedAmountCents ?? 0),
        Currency = payment.Currency,
        Status = refund.Status.ToString(),
        State = refund.State,
        Idempotent = idempotent
    };

    private static Address BuildShippingAddress(ShippingAddressDto? dto)
    {
        return new Address(
            FirstNonEmpty(dto?.Street, dto?.Line1, "123 Main Street"),
            FirstNonEmpty(dto?.City, "Seattle"),
            FirstNonEmpty(dto?.State, dto?.Region, "WA"),
            FirstNonEmpty(dto?.Country, "USA"),
            FirstNonEmpty(dto?.ZipCode, dto?.PostalCode, "98101"));
    }

    private static ResolvedCard ResolveCard(CardDetailsDto? nested, string? number, int? expirationMonth,
        int? expirationYear, string? securityCode, string? cvc, string? cvv, string? name, CardAddressDto? address)
    {
        number = FirstNonEmpty(nested?.Number, number);
        if (string.IsNullOrWhiteSpace(number))
        {
            throw PaymentException.BadRequest("Card number is required.");
        }

        var normalized = new string(number.Where(char.IsDigit).ToArray());
        if (normalized.Length is < 12 or > 19)
        {
            throw PaymentException.BadRequest("Card number is not valid.");
        }

        var month = nested?.ExpirationMonth ?? expirationMonth;
        var year = nested?.ExpirationYear ?? expirationYear;
        if (month is null || month < 1 || month > 12)
        {
            throw PaymentException.BadRequest("Card expiration month is required and must be between 1 and 12.");
        }

        if (year is null || year < 1)
        {
            throw PaymentException.BadRequest("Card expiration year is required.");
        }

        var normalizedYear = year.Value < 100 ? 2000 + year.Value : year.Value;

        var cardAddress = nested?.Address ?? address;
        var security = FirstNonEmpty(nested?.SecurityCode, nested?.Cvc, nested?.Cvv, securityCode, cvc, cvv);
        return new ResolvedCard
        {
            Number = normalized,
            ExpirationMonth = month.Value,
            ExpirationYear = normalizedYear,
            SecurityCode = string.IsNullOrWhiteSpace(security) ? null : security,
            Name = FirstNonEmpty(nested?.Name, name, "eShop Shopper"),
            Address = cardAddress is null ? null : new FinixAddress
            {
                Line1 = NullIfEmpty(FirstNonEmpty(cardAddress.Line1, cardAddress.Street)),
                Line2 = NullIfEmpty(cardAddress.Line2),
                City = NullIfEmpty(cardAddress.City),
                Region = NullIfEmpty(FirstNonEmpty(cardAddress.Region, cardAddress.State)),
                PostalCode = NullIfEmpty(FirstNonEmpty(cardAddress.PostalCode, cardAddress.ZipCode)),
                Country = NullIfEmpty(cardAddress.Country)
            }
        };
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return string.Empty;
    }

    private static long ToCents(decimal amount) =>
        (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static decimal ToMoney(long cents) => cents / 100m;

    private static SemaphoreSlim GetOrderLock(int orderId) =>
        OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));

    private static SemaphoreSlim GetCustomerLock(string ownerId) =>
        CustomerLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));

    private sealed class ResolvedCard
    {
        public string Number { get; set; } = string.Empty;
        public int ExpirationMonth { get; set; }
        public int ExpirationYear { get; set; }
        public string? SecurityCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public FinixAddress? Address { get; set; }
    }
}
