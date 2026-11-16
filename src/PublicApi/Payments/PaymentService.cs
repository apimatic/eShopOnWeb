using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private readonly CatalogContext _db;
    private readonly FinixGateway _gateway;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(CatalogContext db, FinixGateway gateway, ILogger<PaymentService> logger)
    {
        _db = db;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(
        string buyerId,
        PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The caller identity is missing from the token.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        var requestedIds = request.Items.Select(item => item.CatalogItemId).Distinct().ToList();
        var catalogItems = await _db.CatalogItems
            .Where(item => requestedIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        if (catalogItems.Count != requestedIds.Count)
        {
            throw new PaymentValidationException("One or more catalog item ids do not exist.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems[line.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : catalogItem.PictureUri;

            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price,
                line.Quantity));
        }

        var order = new Order(buyerId, BuildAddress(request.ShipToAddress), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, _gateway.Currency);
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Order {OrderId} placed by {BuyerId} for {Total} {Currency}.",
            order.Id, buyerId, order.Total(), payment.Currency);

        return new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total(),
            Status = payment.Status.ToString()
        };
    }

    public async Task<PayOrderResponse> PayAsync(
        string buyerId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (order, payment) = await GetOwnedOrderAsync(buyerId, request.OrderId, cancellationToken);

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return BuildPayResponse(request.CorrelationId(), payment);
        }

        if (payment.Status is PaymentStatus.Cancelled or PaymentStatus.PaymentFailed)
        {
            throw new PaymentConflictException($"Order {order.Id} is in state {payment.Status} and cannot be paid.");
        }

        var hasCard = request.Card is not null;
        var hasSavedCard = request.PaymentMethodId.HasValue;
        if (hasCard == hasSavedCard)
        {
            throw new PaymentValidationException("Provide either card details or paymentMethodId, but not both.");
        }

        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The order total must be greater than zero.");
        }

        var claimKey = $"pay:{order.Id}";
        var finixIdempotencyId = $"pay:{order.Id}:{payment.PaymentReference}";
        if (!await TryClaimAsync(claimKey, "pay", order.Id.ToString(), cancellationToken))
        {
            await _db.Entry(payment).ReloadAsync(cancellationToken);
            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured)
            {
                return BuildPayResponse(request.CorrelationId(), payment);
            }

            throw new PaymentConflictException("A payment attempt for this order is already in progress.");
        }

        try
        {
            string sourcePaymentInstrumentId;
            string? cardBrand;
            string? cardLast4;

            if (hasSavedCard)
            {
                var saved = await _db.SavedPaymentMethods
                    .FirstOrDefaultAsync(method =>
                        method.Id == request.PaymentMethodId!.Value &&
                        method.BuyerId == buyerId &&
                        !method.IsDeleted, cancellationToken)
                    ?? throw new PaymentNotFoundException("The saved payment method was not found.");

                var profile = await GetOrCreateProfileAsync(buyerId, cancellationToken);
                var instrument = await _gateway.GetPaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);

                if (instrument.Enabled == false ||
                    !string.Equals(instrument.IdentityId, profile.FinixIdentityId, StringComparison.Ordinal))
                {
                    throw new PaymentValidationException("The saved payment method is no longer usable.");
                }

                sourcePaymentInstrumentId = saved.FinixPaymentInstrumentId;
                cardBrand = saved.Brand;
                cardLast4 = saved.Last4;
            }
            else
            {
                var card = ToFinixCard(request.Card!);
                var profile = await GetOrCreateProfileAsync(buyerId, cancellationToken);
                var instrument = await _gateway.CreatePaymentInstrumentAsync(
                    profile.FinixIdentityId, card, null, cancellationToken);

                sourcePaymentInstrumentId = instrument.Id;
                cardBrand = instrument.Brand;
                cardLast4 = instrument.Last4;
            }

            FinixAuthorizationResult authorization;
            try
            {
                authorization = await _gateway.CreateAuthorizationAsync(
                    amountCents, sourcePaymentInstrumentId, finixIdempotencyId, cancellationToken);
            }
            catch (PaymentProviderException ex) when (ex.OutcomeUnknown)
            {
                authorization = await SettleUnknownAuthorizationAsync(finixIdempotencyId, ex, cancellationToken);
            }

            if (IsFailed(authorization.State))
            {
                payment.MarkAuthorizationFailed(authorization.FailureCode, authorization.FailureMessage);
                await _db.SaveChangesAsync(cancellationToken);
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw new PaymentException(
                    authorization.FailureMessage ?? "Finix declined the authorization.",
                    HttpStatusCode.PaymentRequired,
                    authorization.FailureCode);
            }

            if (authorization.Amount.HasValue && authorization.Amount.Value != amountCents)
            {
                var reason = $"Finix authorized {authorization.Amount.Value} cents instead of the order total of {amountCents} cents.";
                payment.MarkCaptureUnavailable(reason);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentProviderException(reason, HttpStatusCode.BadGateway);
            }

            payment.MarkAuthorized(
                authorization.Id,
                authorization.State,
                amountCents,
                sourcePaymentInstrumentId,
                cardBrand,
                cardLast4);

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Order {OrderId} authorized by {BuyerId}: authorization {AuthorizationId}, {AmountCents} {Currency}.",
                order.Id, buyerId, payment.FinixAuthorizationId, amountCents, payment.Currency);

            return BuildPayResponse(request.CorrelationId(), payment);
        }
        catch (PaymentProviderException ex) when (!ex.OutcomeUnknown && IsClientError(ex.StatusCode))
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    public async Task<FulfilOrderResponse> FulfilAsync(
        FulfilOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (order, payment) = await GetOrderForOperatorAsync(request.OrderId, cancellationToken);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            await RefreshCaptureDetailsAsync(payment, cancellationToken);
            return BuildFulfilResponse(request.CorrelationId(), payment);
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentConflictException("A cancelled order cannot be fulfilled.");
        }

        if (payment.Status == PaymentStatus.PaymentFailed)
        {
            throw new PaymentConflictException("The order's payment failed and cannot be captured.");
        }

        if (payment.Status is not (PaymentStatus.Authorized or PaymentStatus.CaptureUnavailable) ||
            string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentConflictException("The order has not been authorized and cannot be fulfilled.");
        }

        var claimKey = $"capture:{order.Id}";
        if (!await TryClaimAsync(claimKey, "capture", order.Id.ToString(), cancellationToken))
        {
            await _db.Entry(payment).ReloadAsync(cancellationToken);
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return BuildFulfilResponse(request.CorrelationId(), payment);
            }

            throw new PaymentConflictException("A capture attempt for this order is already in progress.");
        }

        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            var unavailableReason = GetCaptureUnavailableReason(authorization);
            if (unavailableReason is not null)
            {
                payment.MarkCaptureUnavailable(unavailableReason);
                await _db.SaveChangesAsync(cancellationToken);
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw new PaymentConflictException(unavailableReason);
            }

            var captureAmount = payment.AuthorizedAmountCents;
            FinixAuthorizationResult captured;
            try
            {
                captured = await _gateway.CaptureAuthorizationAsync(
                    payment.FinixAuthorizationId!, captureAmount, cancellationToken);
            }
            catch (PaymentProviderException ex) when (ex.OutcomeUnknown)
            {
                captured = await SettleUnknownCaptureAsync(payment, ex, cancellationToken);
            }

            if (IsFailed(captured.State))
            {
                var reason = captured.FailureMessage ?? "Finix refused the capture.";
                payment.MarkCaptureUnavailable(reason);
                await _db.SaveChangesAsync(cancellationToken);
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw new PaymentConflictException(reason);
            }

            var captureTransferId = captured.TransferId;
            if (string.IsNullOrWhiteSpace(captureTransferId))
            {
                var reread = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
                captureTransferId = reread.TransferId;
            }

            if (string.IsNullOrWhiteSpace(captureTransferId))
            {
                const string reason =
                    "Finix accepted the capture but did not return the capture transfer id. Reconcile this order before shipping.";
                payment.MarkCaptureUnavailable(reason);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentProviderException(reason, HttpStatusCode.BadGateway);
            }

            var transfer = await _gateway.GetTransferAsync(captureTransferId!, cancellationToken);
            transfer = await WaitForTransferSettlementAsync(captureTransferId!, transfer, cancellationToken);
            var capturedAmountCents = transfer.Amount ?? captureAmount;
            var fees = await _gateway.GetFeesAsync(captureTransferId!, cancellationToken);
            var feeCents = fees.AmountCents;
            if (feeCents == 0 && transfer.Fee.HasValue)
            {
                feeCents = transfer.Fee.Value;
            }

            var netProceedsCents = capturedAmountCents - feeCents;

            payment.MarkCaptured(
                captureTransferId!,
                transfer.State,
                capturedAmountCents,
                feeCents,
                netProceedsCents);

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Order {OrderId} captured: transfer {CaptureTransferId}, {CapturedAmountCents} cents, fee {FeeCents} cents, net {NetProceedsCents} cents.",
                order.Id, captureTransferId, capturedAmountCents, feeCents, netProceedsCents);

            var response = BuildFulfilResponse(request.CorrelationId(), payment);
            response.FeeComplete = fees.Complete;
            return response;
        }
        catch (PaymentProviderException ex) when (!ex.OutcomeUnknown && IsClientError(ex.StatusCode))
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    public async Task<CancelOrderResponse> CancelAsync(
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (order, payment) = await GetOrderForOperatorAsync(request.OrderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return BuildCancelResponse(request.CorrelationId(), payment);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentConflictException("The order has already been captured; use the refunds endpoint instead.");
        }

        if (payment.Status == PaymentStatus.PaymentFailed)
        {
            payment.MarkVoided("NO_HOLD");
            await _db.SaveChangesAsync(cancellationToken);
            return BuildCancelResponse(request.CorrelationId(), payment);
        }

        if (payment.Status is not (PaymentStatus.Authorized or PaymentStatus.CaptureUnavailable) ||
            string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentConflictException("The order has no held authorization to release.");
        }

        var claimKey = $"void:{order.Id}";
        if (!await TryClaimAsync(claimKey, "void", order.Id.ToString(), cancellationToken))
        {
            await _db.Entry(payment).ReloadAsync(cancellationToken);
            if (payment.Status == PaymentStatus.Cancelled)
            {
                return BuildCancelResponse(request.CorrelationId(), payment);
            }

            throw new PaymentConflictException("A cancellation attempt for this order is already in progress.");
        }

        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            if (authorization.IsVoid == true ||
                string.Equals(authorization.State, "CANCELED", StringComparison.Ordinal) ||
                (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow))
            {
                payment.MarkVoided(authorization.VoidState ?? authorization.State);
                await _db.SaveChangesAsync(cancellationToken);
                return BuildCancelResponse(request.CorrelationId(), payment);
            }

            FinixAuthorizationResult voided;
            try
            {
                voided = await _gateway.VoidAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            }
            catch (PaymentProviderException ex) when (ex.OutcomeUnknown)
            {
                voided = await SettleUnknownVoidAsync(payment, ex, cancellationToken);
            }

            var released = voided.IsVoid == true ||
                string.Equals(voided.VoidState, "SUCCEEDED", StringComparison.Ordinal) ||
                string.Equals(voided.State, "CANCELED", StringComparison.Ordinal);

            if (!released)
            {
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw new PaymentConflictException(
                    $"Finix did not confirm the void (state {voided.State ?? "UNKNOWN"}, void_state {voided.VoidState ?? "UNKNOWN"}). " +
                    (voided.FailureMessage ?? "Retry the cancellation."));
            }

            payment.MarkVoided(voided.VoidState ?? voided.State);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Order {OrderId} cancelled; authorization {AuthorizationId} released.",
                order.Id, payment.FinixAuthorizationId);

            return BuildCancelResponse(request.CorrelationId(), payment);
        }
        catch (PaymentProviderException ex) when (!ex.OutcomeUnknown && IsClientError(ex.StatusCode))
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    public async Task<RefundOrderResponse> RefundAsync(
        string buyerId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (order, payment) = await GetOwnedOrderAsync(buyerId, request.OrderId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new PaymentConflictException("Only a captured order can be refunded.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("idempotencyKey is required for a refund.");
        }

        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            throw new PaymentConflictException("The order has no capture to refund.");
        }

        var amountCents = ResolveRefundAmount(request, payment);
        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The refund amount must be greater than zero.");
        }

        var claimKey = $"refund:{order.Id}:{request.IdempotencyKey}";
        if (!await TryClaimAsync(claimKey, "refund", order.Id.ToString(), cancellationToken))
        {
            var existing = await _db.PaymentRefunds
                .FirstOrDefaultAsync(refund =>
                    refund.OrderPaymentId == payment.Id &&
                    refund.IdempotencyKey == request.IdempotencyKey, cancellationToken);

            if (existing is not null)
            {
                return BuildRefundResponse(request.CorrelationId(), payment, existing);
            }

            throw new PaymentConflictException("A refund with this idempotency key is already in progress.");
        }

        try
        {
            await EnsureCaptureSettledAsync(payment, amountCents, cancellationToken);

            var reversals = await _gateway.ListRefundsAsync(payment.FinixCaptureTransferId!, cancellationToken);
            if (reversals.Truncated)
            {
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw new PaymentProviderException(
                    "Finix returned more refund pages than this integration can safely count; the refund was not attempted.",
                    HttpStatusCode.BadGateway);
            }

            var alreadyRefundedCents = reversals.Items
                .Where(IsCountedRefund)
                .Sum(refund => refund.Amount ?? 0);

            var stillRefundableCents = payment.CapturedAmountCents - alreadyRefundedCents;
            if (amountCents > stillRefundableCents)
            {
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw new PaymentConflictException(
                    $"A refund of {amountCents} cents exceeds the {Math.Max(0, stillRefundableCents)} cents still refundable on this capture.");
            }

            FinixTransferResult refundResult;
            try
            {
                refundResult = await _gateway.CreateRefundAsync(
                    payment.FinixCaptureTransferId!, amountCents, cancellationToken);
            }
            catch (PaymentProviderException ex) when (ex.OutcomeUnknown)
            {
                refundResult = await SettleUnknownRefundAsync(
                    payment, amountCents, request.IdempotencyKey, ex, cancellationToken);
            }

            if (IsFailed(refundResult.State))
            {
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw new PaymentProviderException(
                    refundResult.FailureMessage ?? "Finix refused the refund.",
                    HttpStatusCode.BadGateway,
                    refundResult.FailureCode);
            }

            var record = new PaymentRefund(
                payment.Id,
                request.IdempotencyKey,
                amountCents,
                refundResult.Id,
                refundResult.State);

            _db.PaymentRefunds.Add(record);
            payment.MarkRefunded(amountCents);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Order {OrderId} refunded {AmountCents} cents via reversal {RefundId}; total refunded {RefundedTotalCents} cents.",
                order.Id, amountCents, refundResult.Id, payment.RefundedAmountCents);

            return BuildRefundResponse(request.CorrelationId(), payment, record);
        }
        catch (PaymentProviderException ex) when (!ex.OutcomeUnknown && IsClientError(ex.StatusCode))
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(
        string buyerId,
        GetMyOrdersRequest request,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(order => order.OrderItems)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Where(payment => payment.BuyerId == buyerId)
            .ToDictionaryAsync(payment => payment.OrderId, cancellationToken);

        var response = new MyOrdersResponse(request.CorrelationId());
        foreach (var order in orders)
        {
            payments.TryGetValue(order.Id, out var payment);
            response.Orders.Add(MapSummary(order, payment));
        }

        return response;
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var card = ToFinixCard(request.Card);
        var claimKey = $"savecard:{buyerId}:{card.Bin}:{card.Last4}:{card.ExpirationMonth}:{card.ExpirationYear}";

        if (!await TryClaimAsync(claimKey, "savecard", null, cancellationToken))
        {
            var existing = await _db.SavedPaymentMethods
                .AsNoTracking()
                .FirstOrDefaultAsync(method =>
                    method.BuyerId == buyerId &&
                    !method.IsDeleted &&
                    method.Last4 == card.Last4 &&
                    method.ExpirationMonth == card.ExpirationMonth &&
                    method.ExpirationYear == card.ExpirationYear, cancellationToken);

            if (existing is not null)
            {
                return BuildSavePaymentMethodResponse(request.CorrelationId(), existing);
            }

            throw new PaymentConflictException("This card is already being saved.");
        }

        try
        {
            var profile = await GetOrCreateProfileAsync(buyerId, cancellationToken);

            FinixInstrumentResult instrument;
            try
            {
                instrument = await _gateway.CreatePaymentInstrumentAsync(
                    profile.FinixIdentityId, card, claimKey, cancellationToken);
            }
            catch (PaymentProviderException ex) when (ex.OutcomeUnknown)
            {
                instrument = await SettleUnknownSaveCardAsync(profile, claimKey, card, ex, cancellationToken);
            }

            var saved = new SavedPaymentMethod(
                buyerId,
                profile.FinixIdentityId,
                instrument.Id,
                instrument.Brand,
                instrument.Last4,
                instrument.ExpirationMonth,
                instrument.ExpirationYear,
                card.Name);

            _db.SavedPaymentMethods.Add(saved);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Saved payment method {PaymentMethodId} for {BuyerId}: {Brand} ****{Last4}.",
                saved.Id, buyerId, saved.Brand, saved.Last4);

            return BuildSavePaymentMethodResponse(request.CorrelationId(), saved);
        }
        catch (PaymentProviderException ex) when (!ex.OutcomeUnknown && IsClientError(ex.StatusCode))
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    public async Task<ListPaymentMethodsResponse> ListPaymentMethodsAsync(
        string buyerId,
        ListPaymentMethodsRequest request,
        CancellationToken cancellationToken)
    {
        var methods = await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(method => method.BuyerId == buyerId && !method.IsDeleted)
            .OrderByDescending(method => method.CreatedAt)
            .ToListAsync(cancellationToken);

        var response = new ListPaymentMethodsResponse(request.CorrelationId());
        response.PaymentMethods.AddRange(methods.Select(MapPaymentMethod));
        return response;
    }

    public async Task<DeletePaymentMethodResponse> DeletePaymentMethodAsync(
        string buyerId,
        DeletePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await _db.SavedPaymentMethods
            .FirstOrDefaultAsync(method => method.Id == request.PaymentMethodId && method.BuyerId == buyerId, cancellationToken)
            ?? throw new PaymentNotFoundException("The saved payment method was not found.");

        var response = new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Deleted = true
        };

        if (saved.IsDeleted)
        {
            return response;
        }

        var claimKey = $"delcard:{buyerId}:{saved.Id}";
        if (!await TryClaimAsync(claimKey, "delcard", null, cancellationToken))
        {
            await _db.Entry(saved).ReloadAsync(cancellationToken);
            if (saved.IsDeleted)
            {
                return response;
            }

            throw new PaymentConflictException("This saved payment method is already being deleted.");
        }

        try
        {
            try
            {
                await _gateway.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            }
            catch (PaymentProviderException ex) when (ex.OutcomeUnknown)
            {
                try
                {
                    var instrument = await _gateway.GetPaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
                    if (instrument.Enabled != false)
                    {
                        await ReleaseClaimAsync(claimKey, cancellationToken);
                        throw;
                    }
                }
                catch (PaymentProviderException reread) when (reread.StatusCode == HttpStatusCode.NotFound)
                {
                    // The instrument is already gone at Finix; finish the local delete.
                }
            }
            catch (PaymentProviderException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Already gone at Finix; finish the local delete.
            }

            saved.MarkDeleted();
            await _db.SaveChangesAsync(cancellationToken);
            return response;
        }
        catch (PaymentProviderException ex) when (!ex.OutcomeUnknown && IsClientError(ex.StatusCode) && ex.StatusCode != HttpStatusCode.NotFound)
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        ReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.To < request.From)
        {
            throw new PaymentValidationException("'to' must be greater than or equal to 'from'.");
        }

        var transfers = await _gateway.ListTransfersAsync(request.From, request.To, cancellationToken);
        var authorizations = await _gateway.ListAuthorizationsAsync(request.From, request.To, cancellationToken);

        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Where(payment => payment.CreatedAt >= request.From && payment.CreatedAt <= request.To)
            .ToListAsync(cancellationToken);

        var paymentIds = payments.Select(payment => payment.Id).ToList();
        var refunds = await _db.PaymentRefunds
            .AsNoTracking()
            .Where(refund => paymentIds.Contains(refund.OrderPaymentId))
            .ToListAsync(cancellationToken);

        var paymentByAuthorizationId = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            .ToDictionary(payment => payment.FinixAuthorizationId!, StringComparer.Ordinal);

        var paymentByCaptureTransferId = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            .ToDictionary(payment => payment.FinixCaptureTransferId!, StringComparer.Ordinal);

        var paymentByRefundId = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        foreach (var refund in refunds)
        {
            var payment = payments.FirstOrDefault(item => item.Id == refund.OrderPaymentId);
            if (payment is not null && !string.IsNullOrWhiteSpace(refund.FinixReversalTransferId))
            {
                paymentByRefundId[refund.FinixReversalTransferId] = payment;
            }
        }

        var paymentByInstrumentId = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.FinixPaymentInstrumentId))
            .GroupBy(payment => payment.FinixPaymentInstrumentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var paymentByPayIdempotencyId = payments
            .ToDictionary(payment => $"pay:{payment.OrderId}:{payment.PaymentReference}", payment => payment, StringComparer.Ordinal);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To,
            Truncated = transfers.Truncated || authorizations.Truncated,
            NextCursor = transfers.NextCursor ?? authorizations.NextCursor
        };

        var matchedOrderIds = new HashSet<int>();

        foreach (var authorization in authorizations.Items)
        {
            OrderPayment? payment = null;
            if (!string.IsNullOrWhiteSpace(authorization.Id))
            {
                paymentByAuthorizationId.TryGetValue(authorization.Id, out payment);
            }

            if (payment is null && !string.IsNullOrWhiteSpace(authorization.IdempotencyId))
            {
                paymentByPayIdempotencyId.TryGetValue(authorization.IdempotencyId, out payment);
            }

            var dto = new ReconciliationFinixTransactionDto
            {
                Kind = "AUTHORIZATION",
                Id = authorization.Id,
                AmountCents = authorization.Amount,
                State = authorization.State,
                Type = null,
                IdempotencyId = authorization.IdempotencyId,
                ParentTransferId = authorization.TransferId,
                CreatedAt = null,
                Matched = payment is not null,
                OrderId = payment?.OrderId.ToString()
            };

            response.FinixTransactions.Add(dto);
            if (payment is not null)
            {
                matchedOrderIds.Add(payment.OrderId);
            }
            else
            {
                response.UnmatchedFinixTransactions.Add(dto);
            }
        }

        foreach (var transfer in transfers.Items)
        {
            OrderPayment? payment = null;
            if (!string.IsNullOrWhiteSpace(transfer.Id))
            {
                paymentByCaptureTransferId.TryGetValue(transfer.Id, out payment);
                if (payment is null)
                {
                    paymentByRefundId.TryGetValue(transfer.Id, out payment);
                }
            }

            if (payment is null && !string.IsNullOrWhiteSpace(transfer.ParentTransfer))
            {
                paymentByCaptureTransferId.TryGetValue(transfer.ParentTransfer, out payment);
            }

            if (payment is null && !string.IsNullOrWhiteSpace(transfer.IdempotencyId))
            {
                paymentByPayIdempotencyId.TryGetValue(transfer.IdempotencyId, out payment);
            }

            if (payment is null && !string.IsNullOrWhiteSpace(transfer.ParentTransfer))
            {
                paymentByInstrumentId.TryGetValue(transfer.ParentTransfer, out payment);
            }

            var dto = new ReconciliationFinixTransactionDto
            {
                Kind = "TRANSFER",
                Id = transfer.Id,
                AmountCents = transfer.Amount,
                State = transfer.State,
                Type = transfer.Type,
                IdempotencyId = transfer.IdempotencyId,
                ParentTransferId = transfer.ParentTransfer,
                CreatedAt = transfer.CreatedAt,
                Matched = payment is not null,
                OrderId = payment?.OrderId.ToString()
            };

            response.FinixTransactions.Add(dto);
            if (payment is not null)
            {
                matchedOrderIds.Add(payment.OrderId);
            }
            else
            {
                response.UnmatchedFinixTransactions.Add(dto);
            }
        }

        var finixAuthorizationIds = authorizations.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id!)
            .ToHashSet(StringComparer.Ordinal);

        var finixTransferIds = transfers.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Select(item => item.Id!)
            .ToHashSet(StringComparer.Ordinal);

        var finixIdempotencyIds = transfers.Items
            .Select(item => item.IdempotencyId)
            .Concat(authorizations.Items.Select(item => item.IdempotencyId))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var payment in payments)
        {
            var refundIds = refunds
                .Where(refund => refund.OrderPaymentId == payment.Id)
                .Select(refund => refund.FinixReversalTransferId)
                .ToList();

            var matched = matchedOrderIds.Contains(payment.OrderId) ||
                (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId) && finixAuthorizationIds.Contains(payment.FinixAuthorizationId!)) ||
                (!string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId) && finixTransferIds.Contains(payment.FinixCaptureTransferId!)) ||
                refundIds.Any(id => finixTransferIds.Contains(id)) ||
                finixIdempotencyIds.Contains($"pay:{payment.OrderId}:{payment.PaymentReference}");

            var dto = new ReconciliationEshopOrderDto
            {
                OrderId = payment.OrderId,
                BuyerId = payment.BuyerId,
                Status = payment.Status.ToString(),
                AuthorizationId = payment.FinixAuthorizationId,
                CaptureTransferId = payment.FinixCaptureTransferId,
                RefundIds = refundIds,
                AuthorizedAmountCents = payment.AuthorizedAmountCents,
                CapturedAmountCents = payment.CapturedAmountCents,
                RefundedAmountCents = payment.RefundedAmountCents,
                Matched = matched
            };

            response.EshopOrders.Add(dto);
            if (!matched)
            {
                response.UnmatchedEshopOrders.Add(dto);
            }
        }

        return response;
    }

    private async Task<FinixTransferResult> WaitForTransferSettlementAsync(
        string transferId,
        FinixTransferResult transfer,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        while (string.Equals(transfer.State, "PENDING", StringComparison.Ordinal) &&
               DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _gateway.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task EnsureCaptureSettledAsync(
        OrderPayment payment,
        long requestedAmountCents,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId) ||
            !string.Equals(payment.CaptureState, "PENDING", StringComparison.Ordinal))
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var transfer = await _gateway.GetTransferAsync(payment.FinixCaptureTransferId!, cancellationToken);
            if (!string.Equals(transfer.State, "PENDING", StringComparison.Ordinal))
            {
                var capturedAmountCents = transfer.Amount ?? payment.CapturedAmountCents;
                var fees = await _gateway.GetFeesAsync(payment.FinixCaptureTransferId!, cancellationToken);
                var feeCents = fees.AmountCents;
                if (feeCents == 0 && transfer.Fee.HasValue)
                {
                    feeCents = transfer.Fee.Value;
                }

                payment.UpdateCaptureDetails(
                    transfer.State,
                    capturedAmountCents,
                    feeCents,
                    capturedAmountCents - feeCents);

                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (requestedAmountCents >= payment.CapturedAmountCents)
            {
                // Finix allows a full reversal while the capture is still pending.
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        if (requestedAmountCents < payment.CapturedAmountCents)
        {
            throw new PaymentConflictException(
                $"Finix is still processing the capture (transfer {payment.FinixCaptureTransferId} is PENDING). " +
                "Partial refunds are available once the capture succeeds; retry in a few seconds, or request a full refund.");
        }
    }

    private async Task RefreshCaptureDetailsAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            return;
        }

        try
        {
            var transfer = await _gateway.GetTransferAsync(payment.FinixCaptureTransferId!, cancellationToken);
            var fees = await _gateway.GetFeesAsync(payment.FinixCaptureTransferId!, cancellationToken);
            var capturedAmountCents = transfer.Amount ?? payment.CapturedAmountCents;
            var feeCents = fees.AmountCents;
            if (feeCents == 0 && transfer.Fee.HasValue)
            {
                feeCents = transfer.Fee.Value;
            }

            if (capturedAmountCents != payment.CapturedAmountCents ||
                feeCents != payment.FinixFeeCents ||
                !string.Equals(transfer.State, payment.CaptureState, StringComparison.Ordinal))
            {
                payment.UpdateCaptureDetails(
                    transfer.State,
                    capturedAmountCents,
                    feeCents,
                    capturedAmountCents - feeCents);

                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (PaymentProviderException ex)
        {
            _logger.LogWarning(ex, "Could not refresh capture details for order {OrderId}.", payment.OrderId);
        }
    }

    private async Task<(Order Order, OrderPayment Payment)> GetOwnedOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _db.OrderPayments
            .FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken)
            ?? throw new PaymentNotFoundException($"Order {orderId} has no payment record.");

        return (order, payment);
    }

    private async Task<(Order Order, OrderPayment Payment)> GetOrderForOperatorAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await _db.OrderPayments
            .FirstOrDefaultAsync(item => item.OrderId == orderId, cancellationToken)
            ?? throw new PaymentNotFoundException($"Order {orderId} has no payment record.");

        return (order, payment);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders
            .Include(order => order.OrderItems)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken)
        ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

    private async Task<ShopperFinixProfile> GetOrCreateProfileAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var profile = await _db.ShopperFinixProfiles
            .FirstOrDefaultAsync(item => item.BuyerId == buyerId, cancellationToken);

        if (profile is not null)
        {
            return profile;
        }

        var email = buyerId.Contains('@') ? buyerId : $"{buyerId}@eshop.local";
        var (firstName, lastName) = SplitName(buyerId);

        string identityId;
        try
        {
            identityId = await _gateway.CreateBuyerIdentityAsync(email, firstName, lastName, cancellationToken);
        }
        catch (PaymentProviderException ex) when (ex.OutcomeUnknown)
        {
            var found = await _gateway.FindBuyerIdentityAsync(email, cancellationToken);
            if (string.IsNullOrWhiteSpace(found))
            {
                throw;
            }

            identityId = found!;
        }

        profile = new ShopperFinixProfile(buyerId, identityId);
        _db.ShopperFinixProfiles.Add(profile);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return profile;
        }
        catch (DbUpdateException)
        {
            _db.Entry(profile).State = EntityState.Detached;
            var existing = await _db.ShopperFinixProfiles
                .FirstOrDefaultAsync(item => item.BuyerId == buyerId, cancellationToken);

            return existing
                ?? throw new InvalidOperationException("The shopper's Finix identity could not be saved or loaded.");
        }
    }

    private async Task<bool> TryClaimAsync(
        string claimKey,
        string operation,
        string? orderId,
        CancellationToken cancellationToken)
    {
        var exists = await _db.IdempotencyClaims
            .AsNoTracking()
            .AnyAsync(claim => claim.ClaimKey == claimKey, cancellationToken);

        if (exists)
        {
            return false;
        }

        var claim = new IdempotencyClaim(claimKey, operation, orderId);
        _db.IdempotencyClaims.Add(claim);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _db.Entry(claim).State = EntityState.Detached;

            var nowExists = await _db.IdempotencyClaims
                .AsNoTracking()
                .AnyAsync(item => item.ClaimKey == claimKey, cancellationToken);

            if (nowExists)
            {
                return false;
            }

            throw;
        }
    }

    private async Task ReleaseClaimAsync(string claimKey, CancellationToken cancellationToken)
    {
        var claim = await _db.IdempotencyClaims
            .FirstOrDefaultAsync(item => item.ClaimKey == claimKey, cancellationToken);

        if (claim is null)
        {
            return;
        }

        _db.IdempotencyClaims.Remove(claim);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<FinixAuthorizationResult> SettleUnknownAuthorizationAsync(
        string finixIdempotencyId,
        PaymentProviderException original,
        CancellationToken cancellationToken)
    {
        try
        {
            var found = await _gateway.FindAuthorizationByIdempotencyIdAsync(finixIdempotencyId, cancellationToken);
            if (found is not null)
            {
                _logger.LogWarning(
                    "Authorization for idempotency id {IdempotencyId} was found at Finix after an unknown outcome; adopting it.",
                    finixIdempotencyId);
                return found;
            }
        }
        catch (PaymentProviderException)
        {
            // Fall through to releasing the claim and reporting the original unknown outcome.
        }

        var orderId = finixIdempotencyId.Split(':')[1];
        await ReleaseClaimAsync($"pay:{orderId}", cancellationToken);
        throw original;
    }

    private async Task<FinixAuthorizationResult> SettleUnknownCaptureAsync(
        OrderPayment payment,
        PaymentProviderException original,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            if (!string.IsNullOrWhiteSpace(authorization.TransferId) && !IsFailed(authorization.State))
            {
                _logger.LogWarning(
                    "Capture for order {OrderId} was found at Finix after an unknown outcome; adopting it.",
                    payment.OrderId);
                return authorization;
            }
        }
        catch (PaymentProviderException)
        {
            // Fall through to releasing the claim and reporting the original unknown outcome.
        }

        await ReleaseClaimAsync($"capture:{payment.OrderId}", cancellationToken);
        throw original;
    }

    private async Task<FinixAuthorizationResult> SettleUnknownVoidAsync(
        OrderPayment payment,
        PaymentProviderException original,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            var released = authorization.IsVoid == true ||
                string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.Ordinal) ||
                string.Equals(authorization.State, "CANCELED", StringComparison.Ordinal);

            if (released)
            {
                _logger.LogWarning(
                    "Void for order {OrderId} was found at Finix after an unknown outcome; adopting it.",
                    payment.OrderId);
                return authorization;
            }
        }
        catch (PaymentProviderException)
        {
            // Fall through to releasing the claim and reporting the original unknown outcome.
        }

        await ReleaseClaimAsync($"void:{payment.OrderId}", cancellationToken);
        throw original;
    }

    private async Task<FinixTransferResult> SettleUnknownRefundAsync(
        OrderPayment payment,
        long amountCents,
        string idempotencyKey,
        PaymentProviderException original,
        CancellationToken cancellationToken)
    {
        try
        {
            var claim = await _db.IdempotencyClaims
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.ClaimKey == $"refund:{payment.OrderId}:{idempotencyKey}", cancellationToken);

            var since = claim?.CreatedAt ?? DateTimeOffset.UtcNow.AddMinutes(-5);
            var knownIds = await _db.PaymentRefunds
                .AsNoTracking()
                .Where(refund => refund.OrderPaymentId == payment.Id)
                .Select(refund => refund.FinixReversalTransferId)
                .ToListAsync(cancellationToken);

            var reversals = await _gateway.ListRefundsAsync(payment.FinixCaptureTransferId!, cancellationToken);
            var candidate = reversals.Items.FirstOrDefault(refund =>
                (refund.Amount ?? 0) == amountCents &&
                refund.CreatedAt.HasValue &&
                refund.CreatedAt.Value >= since &&
                !knownIds.Contains(refund.Id));

            if (candidate is not null)
            {
                _logger.LogWarning(
                    "Refund for order {OrderId} was found at Finix after an unknown outcome; adopting reversal {RefundId}.",
                    payment.OrderId, candidate.Id);
                return candidate;
            }
        }
        catch (PaymentProviderException)
        {
            // Fall through to releasing the claim and reporting the original unknown outcome.
        }

        await ReleaseClaimAsync($"refund:{payment.OrderId}:{idempotencyKey}", cancellationToken);
        throw original;
    }

    private async Task<FinixInstrumentResult> SettleUnknownSaveCardAsync(
        ShopperFinixProfile profile,
        string claimKey,
        FinixCardDetails card,
        PaymentProviderException original,
        CancellationToken cancellationToken)
    {
        try
        {
            var instruments = await _gateway.ListPaymentInstrumentsAsync(
                profile.FinixIdentityId, "app_save_key", claimKey, cancellationToken);

            var candidate = instruments.Items.FirstOrDefault(instrument =>
                string.Equals(instrument.Last4, card.Last4, StringComparison.Ordinal) &&
                instrument.ExpirationMonth == card.ExpirationMonth &&
                instrument.ExpirationYear == card.ExpirationYear);

            if (candidate is not null)
            {
                _logger.LogWarning(
                    "Payment instrument for claim {ClaimKey} was found at Finix after an unknown outcome; adopting it.",
                    claimKey);
                return candidate;
            }
        }
        catch (PaymentProviderException)
        {
            // Fall through to releasing the claim and reporting the original unknown outcome.
        }

        await ReleaseClaimAsync(claimKey, cancellationToken);
        throw original;
    }

    private static PaymentSummaryDto MapSummary(Order order, OrderPayment? payment) =>
        new()
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Status = payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString(),
            Currency = payment?.Currency ?? string.Empty,
            AuthorizationId = payment?.FinixAuthorizationId,
            AuthorizationState = payment?.AuthorizationState,
            AuthorizedAmountCents = payment?.AuthorizedAmountCents ?? 0,
            CaptureTransferId = payment?.FinixCaptureTransferId,
            CaptureState = payment?.CaptureState,
            CapturedAmountCents = payment?.CapturedAmountCents ?? 0,
            FinixFeeCents = payment?.FinixFeeCents ?? 0,
            NetProceedsCents = payment?.NetProceedsCents ?? 0,
            RefundedAmountCents = payment?.RefundedAmountCents ?? 0,
            RefundableAmountCents = Math.Max(0, (payment?.CapturedAmountCents ?? 0) - (payment?.RefundedAmountCents ?? 0)),
            CardBrand = payment?.CardBrand,
            CardLast4 = payment?.CardLast4,
            FailureCode = payment?.FailureCode,
            FailureMessage = payment?.FailureMessage,
            CaptureUnavailableReason = payment?.CaptureUnavailableReason
        };

    private static PaymentMethodDto MapPaymentMethod(SavedPaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand ?? "UNKNOWN",
            Last4 = method.Last4 ?? string.Empty,
            ExpirationMonth = method.ExpirationMonth,
            ExpirationYear = method.ExpirationYear,
            CardholderName = method.CardholderName,
            CreatedAt = method.CreatedAt
        };

    private static PayOrderResponse BuildPayResponse(Guid correlationId, OrderPayment payment) =>
        new(correlationId)
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            AuthorizationId = payment.FinixAuthorizationId,
            AuthorizationState = payment.AuthorizationState,
            AuthorizedAmountCents = payment.AuthorizedAmountCents,
            CardBrand = payment.CardBrand,
            CardLast4 = payment.CardLast4,
            FailureCode = payment.FailureCode,
            FailureMessage = payment.FailureMessage
        };

    private static FulfilOrderResponse BuildFulfilResponse(Guid correlationId, OrderPayment payment) =>
        new(correlationId)
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            CaptureTransferId = payment.FinixCaptureTransferId,
            CaptureState = payment.CaptureState,
            CapturedAmountCents = payment.CapturedAmountCents,
            FinixFeeCents = payment.FinixFeeCents,
            NetProceedsCents = payment.NetProceedsCents,
            CaptureUnavailableReason = payment.CaptureUnavailableReason
        };

    private static CancelOrderResponse BuildCancelResponse(Guid correlationId, OrderPayment payment) =>
        new(correlationId)
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            AuthorizationState = payment.AuthorizationState
        };

    private static RefundOrderResponse BuildRefundResponse(
        Guid correlationId,
        OrderPayment payment,
        PaymentRefund refund) =>
        new(correlationId)
        {
            RefundId = refund.FinixReversalTransferId,
            OrderId = payment.OrderId,
            AmountCents = refund.AmountCents,
            RefundedTotalCents = payment.RefundedAmountCents,
            RefundableAmountCents = Math.Max(0, payment.CapturedAmountCents - payment.RefundedAmountCents),
            Status = payment.Status.ToString()
        };

    private static SavePaymentMethodResponse BuildSavePaymentMethodResponse(
        Guid correlationId,
        SavedPaymentMethod method) =>
        new(correlationId)
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand ?? "UNKNOWN",
            Last4 = method.Last4 ?? string.Empty,
            ExpirationMonth = method.ExpirationMonth,
            ExpirationYear = method.ExpirationYear,
            CardholderName = method.CardholderName
        };

    private static Address BuildAddress(AddressRequest? request) =>
        new(
            string.IsNullOrWhiteSpace(request?.Street) ? "N/A" : request!.Street!,
            string.IsNullOrWhiteSpace(request?.City) ? "N/A" : request!.City!,
            string.IsNullOrWhiteSpace(request?.State) ? "N/A" : request!.State!,
            string.IsNullOrWhiteSpace(request?.Country) ? "N/A" : request!.Country!,
            string.IsNullOrWhiteSpace(request?.ZipCode) ? "N/A" : request!.ZipCode!);

    private static FinixCardDetails ToFinixCard(CardDetailsRequest? card)
    {
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length is < 12 or > 19)
        {
            throw new PaymentValidationException("The card number must contain between 12 and 19 digits.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("The card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentValidationException("The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("The cardholder name is required.");
        }

        var securityCode = string.IsNullOrWhiteSpace(card.SecurityCode)
            ? null
            : new string(card.SecurityCode.Where(char.IsDigit).ToArray());

        return new FinixCardDetails(
            number,
            card.ExpirationMonth,
            card.ExpirationYear,
            string.IsNullOrWhiteSpace(securityCode) ? null : securityCode,
            card.Name.Trim());
    }

    private static long ResolveRefundAmount(RefundOrderRequest request, OrderPayment payment)
    {
        if (request.AmountCents.HasValue && request.Amount.HasValue)
        {
            throw new PaymentValidationException("Provide either amountCents or amount, not both.");
        }

        if (request.AmountCents.HasValue)
        {
            return request.AmountCents.Value;
        }

        if (request.Amount.HasValue)
        {
            return ToCents(request.Amount.Value);
        }

        return payment.CapturedAmountCents - payment.RefundedAmountCents;
    }

    private static bool IsFailed(string? state) =>
        string.Equals(state, "FAILED", StringComparison.Ordinal) ||
        string.Equals(state, "CANCELED", StringComparison.Ordinal);

    private static bool IsCountedRefund(FinixTransferResult refund) =>
        string.Equals(refund.Type, "REVERSAL", StringComparison.Ordinal) && !IsFailed(refund.State);

    private static bool IsClientError(HttpStatusCode statusCode) => (int)statusCode is >= 400 and < 500;

    private static string? GetCaptureUnavailableReason(FinixAuthorizationResult authorization)
    {
        if (authorization.IsVoid == true ||
            string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.Ordinal))
        {
            return "The authorization was voided, so the held funds were released and the order can no longer be captured. " +
                "Ask the shopper to pay again.";
        }

        if (IsFailed(authorization.State))
        {
            return $"The authorization is {authorization.State}. {authorization.FailureMessage}".Trim();
        }

        if (string.Equals(authorization.State, "PENDING", StringComparison.Ordinal))
        {
            return "The authorization is still pending at Finix. Retry fulfilment after it succeeds.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"The authorization expired at {authorization.ExpiresAt.Value:O}. The hold can no longer be captured; " +
                "cancel the order and ask the shopper to pay again.";
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.Ordinal))
        {
            return $"The authorization is in state {authorization.State ?? "UNKNOWN"} and cannot be captured.";
        }

        return null;
    }

    private static (string? FirstName, string? LastName) SplitName(string buyerId)
    {
        var localPart = buyerId.Contains('@') ? buyerId[..buyerId.IndexOf('@')] : buyerId;
        var parts = localPart
            .Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 0)
            .ToArray();

        if (parts.Length == 0)
        {
            return (null, null);
        }

        var first = parts[0].Length > 20 ? parts[0][..20] : parts[0];
        var last = parts.Length > 1
            ? (parts[^1].Length > 20 ? parts[^1][..20] : parts[^1])
            : null;

        return (first, last);
    }

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
