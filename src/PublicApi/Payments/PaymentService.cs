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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly PaymentsDbContext _payments;
    private readonly FinixGateway _gateway;
    private readonly FinixSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentClaimStore _claims;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IRepository<Order> orders, IRepository<CatalogItem> catalog, PaymentsDbContext payments,
        FinixGateway gateway, FinixSettings settings, IUriComposer uriComposer, PaymentClaimStore claims,
        ILogger<PaymentService> logger)
    {
        _orders = orders;
        _catalog = catalog;
        _payments = payments;
        _gateway = gateway;
        _settings = settings;
        _uriComposer = uriComposer;
        _claims = claims;
        _logger = logger;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentDomainException(400, "At least one catalog item is required.");
        }

        var lines = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentDomainException(400, "Every item quantity must be greater than zero.");
        }

        var catalogItems = await _catalog.ListAsync(
            new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray()), ct);
        var byId = catalogItems.ToDictionary(i => i.Id);

        var missing = lines.Where(l => !byId.ContainsKey(l.CatalogItemId)).Select(l => l.CatalogItemId).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentDomainException(400,
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var item = byId[line.CatalogItemId];
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
                item.Price,
                line.Quantity));
        }

        var order = new Order(buyerId, ToShippingAddress(request.ShippingAddress), orderItems);
        await _orders.AddAsync(order, ct);

        var totalCents = ToCents(order.Total());
        var payment = new OrderPayment(order.Id, buyerId, totalCents, _settings.Currency);
        _payments.OrderPayments.Add(payment);
        await _payments.SaveChangesAsync(ct);

        _logger.LogInformation("Order {OrderId} created for {BuyerId} awaiting payment ({TotalCents} cents).",
            order.Id, buyerId, totalCents);

        return new CreateOrderResponse
        {
            OrderId = order.Id,
            TotalCents = totalCents,
            Total = order.Total(),
            Currency = _settings.Currency,
            Status = payment.Status.ToString()
        };
    }

    public async Task<PayOrderResponse> PayOrderAsync(int orderId, string buyerId, PayOrderRequest request,
        CancellationToken ct)
    {
        var order = await FindOrderForShopperAsync(orderId, buyerId, ct)
                    ?? throw new PaymentDomainException(404, "Order not found.");

        var payment = await GetOrCreatePaymentAsync(order, ct);

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return MapPay(payment);
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentDomainException(409, "This order was canceled; place a new order to pay again.");
        }

        var (instrumentId, savedCardId, oneOffInstrumentId) =
            await ResolveInstrumentAsync(request, buyerId, ct);

        var claimKey = $"AUTHORIZE:{order.Id}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
        {
            var current = await ReloadPaymentAsync(order.Id, ct);
            if (current.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return MapPay(current);
            }

            throw new PaymentDomainException(409,
                "An authorization for this order is already in progress. Retry once it completes.");
        }

        try
        {
            var idempotencyId = $"eshop-auth-{order.Id}-{payment.PaymentAttemptKey}";
            var authorization = await _gateway.AuthorizeAsync(payment.TotalCents, instrumentId, idempotencyId,
                "ESHOP ORDER", ct);

            payment.MarkAuthorized(authorization.Id, authorization.State, authorization.ExpiresAt, savedCardId,
                oneOffInstrumentId);
            await _payments.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Order {OrderId} authorized {AmountCents} cents (authorization {AuthorizationId}, state {State}).",
                order.Id, payment.TotalCents, authorization.Id, authorization.State);

            return MapPay(payment);
        }
        catch (FinixProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                payment.MarkPaymentUnknown(ex.Code, ex.Message);
                await _payments.SaveChangesAsync(ct);
            }
            else
            {
                payment.MarkPaymentFailed(ex.Code, ex.Message);
                await _payments.SaveChangesAsync(ct);
                await DisableOneOffInstrumentAsync(oneOffInstrumentId, ct);
            }

            throw Translate(ex, "The payment could not be authorized.");
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey, ct);
        }
    }

    public async Task<FulfilOrderResponse> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await FindOrderAsync(orderId, ct)
                    ?? throw new PaymentDomainException(404, "Order not found.");

        var payment = await GetPaymentAsync(order.Id, ct)
                      ?? throw new PaymentDomainException(409, "This order has no payment to fulfil.");

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return MapFulfil(payment);
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentDomainException(409,
                "The hold on this order was released (canceled), so there is nothing to capture. Ask the shopper to pay again.");
        }

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentDomainException(409,
                "This order was never authorized. Ask the shopper to pay before fulfilling it.");
        }

        var claimKey = $"CAPTURE:{order.Id}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
        {
            var current = await ReloadPaymentAsync(order.Id, ct);
            if (current.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return MapFulfil(current);
            }

            throw new PaymentDomainException(409,
                "A capture for this order is already in progress. Retry once it completes.");
        }

        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
            var holdProblem = DescribeHoldProblem(authorization);
            if (holdProblem is not null)
            {
                throw new PaymentDomainException(409, holdProblem);
            }

            if (!string.IsNullOrWhiteSpace(authorization.TransferId))
            {
                var existingTransfer = await _gateway.GetTransferAsync(authorization.TransferId, ct);
                payment.MarkCaptured(existingTransfer.Id, existingTransfer.State,
                    existingTransfer.Amount ?? payment.TotalCents, existingTransfer.Fee);
                await _payments.SaveChangesAsync(ct);
                await DisableOneOffInstrumentAsync(payment.OneOffInstrumentId, ct);
                return MapFulfil(payment);
            }

            var captured = await _gateway.CaptureAsync(payment.AuthorizationId, payment.TotalCents, ct);
            if (string.IsNullOrWhiteSpace(captured.TransferId))
            {
                captured = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
            }

            if (string.IsNullOrWhiteSpace(captured.TransferId))
            {
                throw new PaymentDomainException(502,
                    "Finix accepted the capture but did not return the resulting transfer id. Re-run fulfilment to reconcile it.");
            }

            var transfer = await _gateway.GetTransferAsync(captured.TransferId, ct);
            payment.MarkCaptured(transfer.Id, transfer.State, transfer.Amount ?? payment.TotalCents, transfer.Fee);
            await _payments.SaveChangesAsync(ct);
            await DisableOneOffInstrumentAsync(payment.OneOffInstrumentId, ct);

            _logger.LogInformation(
                "Order {OrderId} captured {AmountCents} cents (transfer {TransferId}, fee {FeeCents}).",
                order.Id, payment.CapturedCents, transfer.Id, transfer.Fee);

            return MapFulfil(payment);
        }
        catch (FinixProviderException ex)
        {
            throw Translate(ex, "The hold could not be captured.");
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey, ct);
        }
    }

    public async Task<CancelOrderResponse> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await FindOrderAsync(orderId, ct)
                    ?? throw new PaymentDomainException(404, "Order not found.");

        var payment = await GetPaymentAsync(order.Id, ct)
                      ?? throw new PaymentDomainException(409, "This order has no payment to cancel.");

        if (payment.Status == PaymentStatus.Canceled)
        {
            return MapCancel(payment);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentDomainException(409,
                "This order has already been fulfilled. Use the refunds endpoint to return captured funds.");
        }

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentDomainException(409, "This order has no held funds to release.");
        }

        var claimKey = $"VOID:{order.Id}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
        {
            var current = await ReloadPaymentAsync(order.Id, ct);
            if (current.Status == PaymentStatus.Canceled)
            {
                return MapCancel(current);
            }

            throw new PaymentDomainException(409,
                "A cancellation for this order is already in progress. Retry once it completes.");
        }

        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
            if (authorization.IsVoid || authorization.VoidState == "SUCCEEDED")
            {
                payment.MarkCanceled(authorization.VoidState ?? authorization.State);
                await _payments.SaveChangesAsync(ct);
                await DisableOneOffInstrumentAsync(payment.OneOffInstrumentId, ct);
                return MapCancel(payment);
            }

            if (!string.IsNullOrWhiteSpace(authorization.TransferId))
            {
                throw new PaymentDomainException(409,
                    "This order has already been captured. Use the refunds endpoint to return captured funds.");
            }

            var voided = await _gateway.VoidAsync(payment.AuthorizationId, ct);
            payment.MarkCanceled(voided.VoidState ?? voided.State);
            await _payments.SaveChangesAsync(ct);
            await DisableOneOffInstrumentAsync(payment.OneOffInstrumentId, ct);

            _logger.LogInformation("Order {OrderId} hold released (authorization {AuthorizationId}).",
                order.Id, payment.AuthorizationId);

            return MapCancel(payment);
        }
        catch (FinixProviderException ex)
        {
            throw Translate(ex, "The hold could not be released.");
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey, ct);
        }
    }

    public async Task<RefundOrderResponse> RefundOrderAsync(int orderId, string buyerId, RefundOrderRequest request,
        CancellationToken ct)
    {
        var order = await FindOrderForShopperAsync(orderId, buyerId, ct)
                    ?? throw new PaymentDomainException(404, "Order not found.");

        var payment = await GetPaymentAsync(order.Id, ct)
                      ?? throw new PaymentDomainException(409, "This order has no captured payment to refund.");

        var key = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PaymentDomainException(400, "An IdempotencyKey is required for a refund.");
        }

        var priorRefund = await _payments.RefundRecords
            .FirstOrDefaultAsync(r => r.OrderId == order.Id && r.IdempotencyKey == key, ct);
        if (priorRefund is not null)
        {
            if (priorRefund.Status == "UNKNOWN")
            {
                var settled = await TryFindUnconfirmedRefundAsync(payment, priorRefund, ct);
                if (settled is null)
                {
                    throw new PaymentDomainException(409,
                        "The earlier refund under this idempotency key is still unconfirmed by Finix. Retry later; it will not be sent twice.");
                }

                priorRefund.MarkConfirmed(settled.Id, settled.State ?? "PENDING");
                payment.RecordRefund(priorRefund.AmountCents);
                await _payments.SaveChangesAsync(ct);
            }

            return MapRefund(priorRefund, payment);
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            throw new PaymentDomainException(409, "This payment has already been fully refunded.");
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentDomainException(409,
                "Only a fulfilled (captured) payment can be refunded.");
        }

        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw new PaymentDomainException(409, "This order has no captured transfer to refund.");
        }

        var amountCents = request.AmountCents ?? payment.RefundableCents;
        if (amountCents <= 0)
        {
            throw new PaymentDomainException(400, "The refund amount must be greater than zero.");
        }

        var refundClaimKey = $"REFUND:{order.Id}:{key}";
        if (!await _claims.TryClaimAsync(refundClaimKey, ct))
        {
            var existing = await _payments.RefundRecords
                .FirstOrDefaultAsync(r => r.OrderId == order.Id && r.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                return MapRefund(existing, payment);
            }

            throw new PaymentDomainException(409,
                "A refund with this idempotency key is already in progress.");
        }

        var serialClaimKey = $"REFUND_SERIAL:{order.Id}";
        if (!await _claims.TryClaimAsync(serialClaimKey, ct))
        {
            await _claims.ReleaseAsync(refundClaimKey, ct);
            throw new PaymentDomainException(409, "Another refund for this order is in progress. Retry once it completes.");
        }

        try
        {
            await _payments.Entry(payment).ReloadAsync(ct);

            if (amountCents > payment.RefundableCents)
            {
                throw new PaymentDomainException(409,
                    $"Refund of {amountCents} cents exceeds the remaining captured amount of {payment.RefundableCents} cents.");
            }

            if (amountCents < payment.CapturedCents)
            {
                await EnsureTransferSettledForPartialRefundAsync(payment, ct);
            }

            var transfer = await _gateway.RefundAsync(payment.CaptureTransferId, amountCents, ct);

            var record = new RefundRecord(order.Id, key, transfer.Id, amountCents, transfer.State ?? "PENDING");
            _payments.RefundRecords.Add(record);
            payment.RecordRefund(amountCents);
            await _payments.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Order {OrderId} refunded {AmountCents} cents (refund {RefundId}, state {State}).",
                order.Id, amountCents, transfer.Id, transfer.State);

            return MapRefund(record, payment);
        }
        catch (FinixProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                _payments.RefundRecords.Add(new RefundRecord(order.Id, key, "UNKNOWN", amountCents, "UNKNOWN"));
                await _payments.SaveChangesAsync(ct);
                throw Translate(ex,
                    "The refund could not be confirmed. It has been recorded and will not be sent again under this idempotency key.");
            }

            await _claims.ReleaseAsync(refundClaimKey, ct);
            throw Translate(ex, "The refund could not be created.");
        }
        catch
        {
            await _claims.ReleaseAsync(refundClaimKey, ct);
            throw;
        }
        finally
        {
            await _claims.ReleaseAsync(serialClaimKey, ct);
        }
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _payments.OrderPayments
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync(ct);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        var response = new MyOrdersResponse();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            byOrder.TryGetValue(order.Id, out var payment);
            response.Orders.Add(MapMyOrder(order, payment));
        }

        return response;
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new PaymentDomainException(400, "The 'to' date-time must not be earlier than 'from'.");
        }

        const int maxPages = 50;
        var transfers = await _gateway.ListTransfersAsync(from, to, maxPages, ct);
        var authorizations = await _gateway.ListAuthorizationsAsync(from, to, maxPages, ct);

        var payments = await _payments.OrderPayments.ToListAsync(ct);
        var refunds = await _payments.RefundRecords.ToListAsync(ct);
        var orders = await _orders.ListAsync(new OrdersInRangeSpecification(from, to), ct);

        var captureByTransfer = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.CaptureTransferId))
            .GroupBy(p => p.CaptureTransferId!)
            .ToDictionary(g => g.Key, g => g.First());
        var refundByTransfer = refunds
            .GroupBy(r => r.FinixTransferId)
            .ToDictionary(g => g.Key, g => g.First());
        var paymentsByAttempt = payments.ToDictionary(
            p => $"{p.OrderId}:{p.PaymentAttemptKey}",
            p => p);

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            Truncated = transfers.Truncated || authorizations.Truncated,
            NextCursor = transfers.NextCursor,
            AuthorizationNextCursor = authorizations.NextCursor
        };

        var finixIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transfer in transfers.Items)
        {
            int? matchedOrderId = null;
            if (captureByTransfer.TryGetValue(transfer.Id, out var capturePayment))
            {
                matchedOrderId = capturePayment.OrderId;
            }
            else if (refundByTransfer.TryGetValue(transfer.Id, out var refund))
            {
                matchedOrderId = refund.OrderId;
            }
            else if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer)
                     && captureByTransfer.TryGetValue(transfer.ParentTransfer, out var parentPayment))
            {
                matchedOrderId = parentPayment.OrderId;
            }

            finixIds.Add(transfer.Id);
            response.FinixTransactions.Add(new FinixTransactionDto
            {
                Id = transfer.Id,
                Kind = transfer.Type ?? "TRANSFER",
                Status = transfer.State,
                AmountCents = transfer.Amount,
                FeeCents = transfer.Fee,
                CreatedAt = transfer.CreatedAt,
                ParentTransferId = transfer.ParentTransfer,
                IdempotencyId = transfer.IdempotencyId,
                MatchedOrderId = matchedOrderId,
                MatchStatus = matchedOrderId.HasValue ? "matched" : "unmatched"
            });
        }

        foreach (var authorization in authorizations.Items)
        {
            var matchedPayment = payments.FirstOrDefault(p => p.AuthorizationId == authorization.Id);
            var matchedOrderId = matchedPayment?.OrderId
                                 ?? OrderIdFromIdempotency(authorization.IdempotencyId, paymentsByAttempt);

            finixIds.Add(authorization.Id);
            response.FinixTransactions.Add(new FinixTransactionDto
            {
                Id = authorization.Id,
                Kind = "AUTHORIZATION",
                Status = authorization.IsVoid ? "VOIDED" : authorization.State,
                AmountCents = authorization.Amount,
                CreatedAt = authorization.CreatedAt,
                IdempotencyId = authorization.IdempotencyId,
                MatchedOrderId = matchedOrderId,
                MatchStatus = matchedOrderId.HasValue ? "matched" : "unmatched"
            });
        }

        foreach (var order in orders)
        {
            var payment = payments.FirstOrDefault(p => p.OrderId == order.Id);
            var authorizationSeen = payment?.AuthorizationId is not null && finixIds.Contains(payment.AuthorizationId);
            var captureSeen = payment?.CaptureTransferId is not null && finixIds.Contains(payment.CaptureTransferId);

            if (payment is not null && (authorizationSeen || captureSeen))
            {
                continue;
            }

            response.EshopOnlyOrders.Add(new EshopOnlyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                TotalCents = payment?.TotalCents ?? ToCents(order.Total()),
                PaymentStatus = payment?.Status.ToString() ?? "NoPaymentRecord",
                AuthorizationId = payment?.AuthorizationId,
                CaptureTransferId = payment?.CaptureTransferId
            });
        }

        return response;
    }

    public async Task<CreatePaymentMethodResponse> SavePaymentMethodAsync(string shopperId, CardRequest card,
        string? requestKey, CancellationToken ct)
    {
        ValidateCard(card);

        if (!string.IsNullOrWhiteSpace(requestKey))
        {
            var existing = await _payments.SavedCards
                .FirstOrDefaultAsync(c => c.ShopperId == shopperId && c.RequestKey == requestKey && !c.IsDeleted, ct);
            if (existing is not null)
            {
                return MapPaymentMethod(existing);
            }
        }

        var claimKey = string.IsNullOrWhiteSpace(requestKey)
            ? null
            : $"SAVE:{shopperId}:{requestKey}";

        if (claimKey is not null && !await _claims.TryClaimAsync(claimKey, ct))
        {
            var existing = await _payments.SavedCards
                .FirstOrDefaultAsync(c => c.ShopperId == shopperId && c.RequestKey == requestKey && !c.IsDeleted, ct);
            if (existing is not null)
            {
                return MapPaymentMethod(existing);
            }

            throw new PaymentDomainException(409, "This saved-card request is already in progress.");
        }

        try
        {
            var identityId = await EnsureIdentityAsync(shopperId, shopperId, ct);
            var requestTag = Guid.NewGuid().ToString("N");
            var instrument = await _gateway.CreateCardInstrumentAsync(identityId, ToCardData(card), requestTag, ct);

            var saved = new SavedCard(shopperId, instrument.Id, instrument.Brand, instrument.LastFour,
                instrument.ExpirationMonth ?? card.ExpirationMonth,
                instrument.ExpirationYear ?? card.ExpirationYear,
                requestKey);
            _payments.SavedCards.Add(saved);
            await _payments.SaveChangesAsync(ct);

            _logger.LogInformation("Shopper {ShopperId} saved payment method {PaymentMethodId} ({Brand} ****{LastFour}).",
                shopperId, saved.Id, saved.Brand, saved.LastFour);

            return MapPaymentMethod(saved);
        }
        catch (FinixProviderException ex)
        {
            throw Translate(ex, "The card could not be saved.");
        }
        finally
        {
            if (claimKey is not null)
            {
                await _claims.ReleaseAsync(claimKey, ct);
            }
        }
    }

    public async Task<ListPaymentMethodsResponse> ListPaymentMethodsAsync(string shopperId, CancellationToken ct)
    {
        var cards = await _payments.SavedCards
            .Where(c => c.ShopperId == shopperId && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var response = new ListPaymentMethodsResponse();
        response.PaymentMethods.AddRange(cards.Select(c => new PaymentMethodDto
        {
            PaymentMethodId = c.Id,
            Brand = c.Brand,
            LastFour = c.LastFour,
            ExpirationMonth = c.ExpirationMonth,
            ExpirationYear = c.ExpirationYear,
            CreatedAt = c.CreatedAt
        }));

        return response;
    }

    public async Task DeletePaymentMethodAsync(string paymentMethodId, string shopperId, CancellationToken ct)
    {
        var card = await _payments.SavedCards.FindAsync(new object[] { paymentMethodId }, ct);
        if (card is null || card.ShopperId != shopperId)
        {
            throw new PaymentDomainException(404, "Saved card not found.");
        }

        if (card.IsDeleted)
        {
            return;
        }

        var claimKey = $"DISABLE:{card.Id}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
        {
            var current = await _payments.SavedCards.FindAsync(new object[] { paymentMethodId }, ct);
            if (current is not null && current.IsDeleted)
            {
                return;
            }

            throw new PaymentDomainException(409, "This saved card is already being removed.");
        }

        try
        {
            await _gateway.DisableInstrumentAsync(card.FinixPaymentInstrumentId, ct);
            card.MarkDeleted();
            await _payments.SaveChangesAsync(ct);

            _logger.LogInformation("Shopper {ShopperId} removed payment method {PaymentMethodId}.", shopperId, card.Id);
        }
        catch (FinixProviderException ex)
        {
            throw Translate(ex, "The saved card could not be removed.");
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey, ct);
        }
    }

    private async Task<(string InstrumentId, string? SavedCardId, string? OneOffInstrumentId)> ResolveInstrumentAsync(
        PayOrderRequest request, string buyerId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.PaymentMethodId))
        {
            var card = await _payments.SavedCards.FindAsync(new object[] { request.PaymentMethodId }, ct);
            if (card is null || card.ShopperId != buyerId || card.IsDeleted)
            {
                throw new PaymentDomainException(404, "Saved card not found.");
            }

            return (card.FinixPaymentInstrumentId, card.Id, null);
        }

        if (request.Card is not null)
        {
            ValidateCard(request.Card);
            var identityId = await EnsureIdentityAsync(buyerId, request.Email ?? buyerId, ct);
            var requestTag = Guid.NewGuid().ToString("N");
            var instrument = await _gateway.CreateCardInstrumentAsync(identityId, ToCardData(request.Card),
                requestTag, ct);
            return (instrument.Id, null, instrument.Id);
        }

        throw new PaymentDomainException(400,
            "Provide either 'paymentMethodId' for a saved card or 'card' for a one-off payment.");
    }

    private async Task<string> EnsureIdentityAsync(string shopperId, string? email, CancellationToken ct)
    {
        var profile = await _payments.ShopperProfiles.FindAsync(new object[] { shopperId }, ct);
        if (profile is not null)
        {
            return profile.FinixIdentityId;
        }

        var claimKey = $"IDENTITY:{shopperId}";
        if (!await _claims.TryClaimAsync(claimKey, ct))
        {
            profile = await _payments.ShopperProfiles.FindAsync(new object[] { shopperId }, ct);
            if (profile is not null)
            {
                return profile.FinixIdentityId;
            }

            throw new PaymentDomainException(409,
                "The shopper's Finix identity is being created. Retry in a moment.");
        }

        try
        {
            profile = await _payments.ShopperProfiles.FindAsync(new object[] { shopperId }, ct);
            if (profile is not null)
            {
                return profile.FinixIdentityId;
            }

            var identityId = await _gateway.CreateBuyerIdentityAsync(shopperId, email, ct);
            _payments.ShopperProfiles.Add(new ShopperProfile(shopperId, identityId));
            await _payments.SaveChangesAsync(ct);
            return identityId;
        }
        catch (FinixProviderException ex)
        {
            throw Translate(ex, "The shopper's Finix identity could not be created.");
        }
        finally
        {
            await _claims.ReleaseAsync(claimKey, ct);
        }
    }

    private async Task<Order?> FindOrderForShopperAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        return order is not null && order.BuyerId == buyerId ? order : null;
    }

    private async Task<Order?> FindOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
    }

    private async Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        return await _payments.OrderPayments.FindAsync(new object[] { orderId }, ct);
    }

    private async Task<OrderPayment> GetOrCreatePaymentAsync(Order order, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(order.Id, ct);
        if (payment is not null)
        {
            return payment;
        }

        payment = new OrderPayment(order.Id, order.BuyerId, ToCents(order.Total()), _settings.Currency);
        _payments.OrderPayments.Add(payment);
        await _payments.SaveChangesAsync(ct);
        return payment;
    }

    private async Task<OrderPayment> ReloadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct)
                      ?? throw new PaymentDomainException(409, "This order has no payment record.");
        await _payments.Entry(payment).ReloadAsync(ct);
        return payment;
    }

    private async Task DisableOneOffInstrumentAsync(string? instrumentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrumentId))
        {
            return;
        }

        try
        {
            await _gateway.DisableInstrumentAsync(instrumentId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "One-off payment instrument {InstrumentId} could not be disabled.", instrumentId);
        }
    }

    private async Task<FinixTransferInfo?> TryFindUnconfirmedRefundAsync(OrderPayment payment, RefundRecord refund,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            return null;
        }

        var reversals = await _gateway.ListReversalsAsync(payment.CaptureTransferId, ct);
        return reversals
            .Where(r => r.Amount == refund.AmountCents)
            .Where(r => r.CreatedAt is null || r.CreatedAt >= refund.CreatedAt.AddMinutes(-5))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();
    }

    private async Task EnsureTransferSettledForPartialRefundAsync(OrderPayment payment, CancellationToken ct)
    {        for (var attempt = 0; attempt < 5; attempt++)
        {
            var transfer = await _gateway.GetTransferAsync(payment.CaptureTransferId!, ct);
            if (string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.UpdateTransfer(transfer.State, transfer.Fee);
                await _payments.SaveChangesAsync(ct);
                return;
            }

            if (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        throw new PaymentDomainException(409,
            "Finix still reports the capture transfer as pending. A partial refund becomes available once the transfer settles; retry later, or refund the full captured amount.");
    }

    private static string? DescribeHoldProblem(FinixAuthorizationInfo authorization)
    {
        if (authorization.IsVoid || authorization.VoidState == "SUCCEEDED")
        {
            return "The hold was released (voided), so it can no longer be captured. Ask the shopper to pay again.";
        }

        if (authorization.State != "SUCCEEDED")
        {
            var detail = string.Join(" ",
                new[] { authorization.FailureCode, authorization.FailureMessage }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            return $"The authorization is {authorization.State ?? "in an unknown state"}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}")}. Ask the shopper to pay again.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"The authorization expired at {authorization.ExpiresAt.Value:O}, so it can no longer be captured. Ask the shopper to pay again.";
        }

        return null;
    }

    private static PaymentDomainException Translate(FinixProviderException ex, string fallback)
    {
        if (ex.OutcomeUnknown)
        {
            return new PaymentDomainException(502,
                $"{fallback} Finix did not confirm the outcome; retry the same operation so it can be reconciled.",
                ex.Code, ex.LogRef, ex);
        }

        var status = ex.StatusCode ?? 502;
        if (status is 401 or 403 or 429 or < 400 or >= 500)
        {
            status = 502;
        }

        return new PaymentDomainException(status, string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message,
            ex.Code, ex.LogRef, ex);
    }

    private static int? OrderIdFromIdempotency(string? idempotencyId,
        IReadOnlyDictionary<string, OrderPayment> paymentsByAttempt)
    {
        const string prefix = "eshop-auth-";
        if (idempotencyId is null || !idempotencyId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = idempotencyId[prefix.Length..];
        var dash = remainder.IndexOf('-');
        if (dash <= 0)
        {
            return null;
        }

        var idText = remainder[..dash];
        var attemptKey = remainder[(dash + 1)..];
        return int.TryParse(idText, out var orderId)
               && paymentsByAttempt.TryGetValue($"{orderId}:{attemptKey}", out var payment)
            ? payment.OrderId
            : null;
    }

    private static void ValidateCard(CardRequest card)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentDomainException(400, "A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentDomainException(400, "ExpirationMonth must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentDomainException(400, "The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentDomainException(400, "The cardholder name is required.");
        }
    }

    private static CardData ToCardData(CardRequest card)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        var address = card.Address;
        return new CardData(
            digits,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.SecurityCode,
            card.Name,
            address?.Street,
            address?.City,
            address?.State,
            address?.Country,
            address?.ZipCode);
    }

    private static Address ToShippingAddress(ShippingAddressRequest? address)
    {
        if (address is null)
        {
            return new Address("123 Main St", "Redmond", "WA", "USA", "98052");
        }

        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City)
            || string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new PaymentDomainException(400,
                "ShippingAddress requires Street, City, Country and ZipCode.");
        }

        return new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
    }

    private static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static PayOrderResponse MapPay(OrderPayment payment)
    {
        return new PayOrderResponse
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            AmountCents = payment.TotalCents,
            Currency = payment.Currency,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            HoldExpiresAt = payment.HoldExpiresAt,
            PaymentMethodId = payment.PaymentMethodId
        };
    }

    private static FulfilOrderResponse MapFulfil(OrderPayment payment)
    {
        return new FulfilOrderResponse
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            CaptureTransferId = payment.CaptureTransferId,
            TransferState = payment.TransferState,
            CapturedAmountCents = payment.CapturedCents,
            FeeCents = payment.FeeCents,
            NetProceedsCents = payment.FeeCents.HasValue ? payment.CapturedCents - payment.FeeCents.Value : null,
            FeePending = !payment.FeeCents.HasValue
        };
    }

    private static CancelOrderResponse MapCancel(OrderPayment payment)
    {
        return new CancelOrderResponse
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            Message = "The hold was released; no funds were captured."
        };
    }

    private static RefundOrderResponse MapRefund(RefundRecord record, OrderPayment payment)
    {
        return new RefundOrderResponse
        {
            RefundId = record.FinixTransferId,
            OrderId = record.OrderId,
            AmountCents = record.AmountCents,
            Status = record.Status,
            TotalRefundedCents = payment.AmountRefundedCents,
            RemainingRefundableCents = payment.RefundableCents
        };
    }

    private static MyOrderDto MapMyOrder(Order order, OrderPayment? payment)
    {
        return new MyOrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            TotalCents = payment?.TotalCents ?? ToCents(order.Total()),
            Currency = payment?.Currency ?? "USD",
            PaymentStatus = payment?.Status.ToString() ?? "NoPaymentRecord",
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationStatus = payment?.AuthorizationStatus,
            CaptureTransferId = payment?.CaptureTransferId,
            TransferState = payment?.TransferState,
            CapturedCents = payment?.CapturedCents ?? 0,
            RefundedCents = payment?.AmountRefundedCents ?? 0,
            RefundableCents = payment?.RefundableCents ?? 0,
            ItemCount = order.OrderItems.Count
        };
    }

    private static CreatePaymentMethodResponse MapPaymentMethod(SavedCard card)
    {
        return new CreatePaymentMethodResponse
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            LastFour = card.LastFour,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear
        };
    }
}
