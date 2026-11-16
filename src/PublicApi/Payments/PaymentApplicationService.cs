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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class PaymentApplicationService : IPaymentApplicationService
{
    private const string AuthorizeOperation = "AUTHORIZE";
    private const string CaptureOperation = "CAPTURE";
    private const string VoidOperation = "VOID";

    private readonly CatalogContext _db;
    private readonly IFinixPaymentGateway _finix;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<PaymentApplicationService> _logger;

    public PaymentApplicationService(
        CatalogContext db,
        IFinixPaymentGateway finix,
        IUriComposer uriComposer,
        ILogger<PaymentApplicationService> logger)
    {
        _db = db;
        _finix = finix;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput shippingAddress,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentOperationException("At least one order line is required.", 400);
        }

        if (lines.Any(line => line.Quantity <= 0))
        {
            throw new PaymentOperationException("Every order line must have a positive quantity.", 400);
        }

        ValidateAddress(shippingAddress);

        var catalogItemIds = lines.Select(line => line.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var missing = catalogItemIds.Except(catalogItems.Select(item => item.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentOperationException(
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.",
                400);
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(item => item.Id == line.CatalogItemId);
            var ordered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(ordered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(
            buyerId,
            new Address(
                shippingAddress.Street,
                shippingAddress.City,
                shippingAddress.State,
                shippingAddress.Country,
                shippingAddress.ZipCode),
            orderItems);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = buyerId,
            Status = PaymentStatuses.AwaitingPayment,
            OrderTotalCents = ToCents(order.Total()),
            IdempotencyId = $"eshop-order-{order.Id}-{Guid.NewGuid():N}-authorize",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} created for {BuyerId} with {TotalCents} cents awaiting payment.",
            order.Id,
            buyerId,
            payment.OrderTotalCents);

        return order.Id;
    }

    public async Task<OrderSummary> PayAsync(
        string buyerId,
        int orderId,
        PayOrderCommand command,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderForBuyerAsync(buyerId, orderId, cancellationToken);
        var payment = await GetOrCreatePaymentAsync(order, cancellationToken);

        if (payment.Status is PaymentStatuses.Captured or PaymentStatuses.Cancelled
            or PaymentStatuses.Refunded or PaymentStatuses.PartiallyRefunded)
        {
            throw new PaymentOperationException(
                $"Order {orderId} cannot be paid because its payment state is {payment.Status}.",
                409);
        }

        if (payment.Status == PaymentStatuses.Authorized &&
            !string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            return MapOrder(order, payment);
        }

        if (payment.Status == PaymentStatuses.OutcomeUnknown &&
            !string.IsNullOrWhiteSpace(payment.IdempotencyId))
        {
            var settled = await TryFindAuthorizationAsync(payment.IdempotencyId, cancellationToken);
            if (settled is not null)
            {
                await ApplyAuthorizationAsync(order, payment, settled, payment.PaymentInstrumentId ?? string.Empty,
                    payment.FinixIdentityId, payment.IdempotencyId!, cancellationToken);
                return MapOrder(order, payment);
            }
        }

        var claim = await TryClaimAsync(orderId, AuthorizeOperation, cancellationToken);
        if (claim is null)
        {
            payment = await ReloadPaymentAsync(order, cancellationToken);
            if (payment.Status == PaymentStatuses.Authorized &&
                !string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                return MapOrder(order, payment);
            }

            throw new PaymentOperationException(
                $"A payment for order {orderId} is already in progress.",
                409);
        }

        try
        {
            var (paymentInstrumentId, identityId) =
                await ResolvePaymentInstrumentAsync(buyerId, command, cancellationToken);

            var amountCents = payment.OrderTotalCents > 0
                ? payment.OrderTotalCents
                : ToCents(order.Total());
            var idempotencyId = string.IsNullOrWhiteSpace(payment.IdempotencyId)
                ? $"eshop-order-{orderId}-{Guid.NewGuid():N}-authorize"
                : payment.IdempotencyId;
            payment.IdempotencyId = idempotencyId;

            FinixAuthorizationResult authorization;
            try
            {
                authorization = await _finix.AuthorizeAsync(
                    paymentInstrumentId,
                    amountCents,
                    idempotencyId,
                    cancellationToken);
            }
            catch (PaymentOperationException exception) when (exception.DuplicateIdempotency)
            {
                var found = await TryFindAuthorizationAsync(idempotencyId, cancellationToken);
                if (found is null)
                {
                    throw;
                }

                authorization = found;
            }
            catch (PaymentOperationException exception) when (exception.OutcomeUnknown)
            {
                var existing = await TryFindAuthorizationAsync(idempotencyId, cancellationToken);
                if (existing is null)
                {
                    payment.Status = PaymentStatuses.OutcomeUnknown;
                    payment.FailureMessage = exception.Message;
                    payment.UpdatedAt = DateTimeOffset.UtcNow;
                    order.MarkOutcomeUnknown();
                    await _db.SaveChangesAsync(cancellationToken);
                    throw;
                }

                authorization = existing;
            }

            await ApplyAuthorizationAsync(
                order,
                payment,
                authorization,
                paymentInstrumentId,
                identityId,
                idempotencyId,
                cancellationToken);

            return MapOrder(order, payment);
        }
        catch (PaymentOperationException exception) when (exception.StatusCode < 500)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            payment.Status = PaymentStatuses.Failed;
            payment.FailureCode = exception.Code;
            payment.FailureMessage = exception.Message;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            order.MarkPaymentFailed();
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OrderSummary> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await GetOrCreatePaymentAsync(order, cancellationToken);

        if (payment.Status == PaymentStatuses.Captured)
        {
            return MapOrder(order, payment);
        }

        if (payment.Status != PaymentStatuses.Authorized ||
            string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentOperationException(
                await DescribeHoldStateAsync(payment, cancellationToken),
                409);
        }

        var claim = await TryClaimAsync(orderId, CaptureOperation, cancellationToken);
        if (claim is null)
        {
            payment = await ReloadPaymentAsync(order, cancellationToken);
            if (payment.Status == PaymentStatuses.Captured)
            {
                return MapOrder(order, payment);
            }

            throw new PaymentOperationException(
                $"A capture for order {orderId} is already in progress.",
                409);
        }

        try
        {
            var amountCents = payment.AuthorizedAmountCents > 0
                ? payment.AuthorizedAmountCents
                : payment.OrderTotalCents;

            FinixCaptureResult capture;
            try
            {
                capture = await _finix.CaptureAsync(
                    payment.AuthorizationId!,
                    amountCents,
                    cancellationToken);
            }
            catch (PaymentOperationException exception) when (exception.OutcomeUnknown)
            {
                var authorization = await TryGetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
                if (authorization is null || string.IsNullOrWhiteSpace(authorization.TransferId))
                {
                    throw;
                }

                var transfer = await _finix.GetTransferAsync(authorization.TransferId!, cancellationToken);
                var capturedAmount = transfer?.AmountCents ?? amountCents;
                var fee = transfer?.FeeCents ?? 0;
                capture = new FinixCaptureResult(
                    authorization.AuthorizationId,
                    authorization.State,
                    authorization.TransferId!,
                    transfer?.State,
                    capturedAmount,
                    fee,
                    capturedAmount - fee);
            }

            ApplyCapture(payment, order, capture);
            await _db.SaveChangesAsync(cancellationToken);
            return MapOrder(order, payment);
        }
        catch (PaymentOperationException exception) when (exception.StatusCode < 500)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            var authorization = await TryGetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            if (authorization is not null)
            {
                payment.AuthorizationState = authorization.State;
                payment.FailureCode = authorization.FailureCode;
                payment.FailureMessage = authorization.FailureMessage;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            throw new PaymentOperationException(
                BuildCaptureFailureMessage(payment, authorization),
                409,
                exception.Code,
                exception.LogRef,
                innerException: exception);
        }
    }

    public async Task<OrderSummary> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await GetOrCreatePaymentAsync(order, cancellationToken);

        if (payment.Status == PaymentStatuses.Cancelled)
        {
            return MapOrder(order, payment);
        }

        if (payment.Status is PaymentStatuses.Captured or PaymentStatuses.PartiallyRefunded or PaymentStatuses.Refunded)
        {
            throw new PaymentOperationException(
                $"Order {orderId} has already been fulfilled. Use the refunds endpoint instead of cancelling.",
                409);
        }

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            payment.Status = PaymentStatuses.Cancelled;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            return MapOrder(order, payment);
        }

        var claim = await TryClaimAsync(orderId, VoidOperation, cancellationToken);
        if (claim is null)
        {
            payment = await ReloadPaymentAsync(order, cancellationToken);
            if (payment.Status == PaymentStatuses.Cancelled)
            {
                return MapOrder(order, payment);
            }

            throw new PaymentOperationException(
                $"A cancellation for order {orderId} is already in progress.",
                409);
        }

        try
        {
            FinixVoidResult voided;
            try
            {
                voided = await _finix.VoidAsync(payment.AuthorizationId!, cancellationToken);
            }
            catch (PaymentOperationException exception) when (exception.OutcomeUnknown)
            {
                var authorization = await TryGetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
                if (authorization is null ||
                    (authorization.IsVoid != true &&
                     !string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase)))
                {
                    throw;
                }

                voided = new FinixVoidResult(
                    authorization.AuthorizationId,
                    authorization.State,
                    authorization.IsVoid ?? false,
                    null);
            }

            payment.AuthorizationState = voided.IsVoid ? "CANCELED" : voided.State;
            payment.Status = PaymentStatuses.Cancelled;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            return MapOrder(order, payment);
        }
        catch (PaymentOperationException exception) when (exception.StatusCode < 500)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            throw;
        }
    }

    public async Task<RefundSummary> RefundAsync(
        string buyerId,
        int orderId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderForBuyerAsync(buyerId, orderId, cancellationToken);
        var payment = await GetOrCreatePaymentAsync(order, cancellationToken);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException("An idempotency key is required for refunds.", 400);
        }

        if (amountCents <= 0)
        {
            throw new PaymentOperationException("The refund amount must be greater than zero.", 400);
        }

        var existingRefund = await _db.RefundRecords.FirstOrDefaultAsync(
            record => record.OrderId == orderId && record.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existingRefund is not null)
        {
            return MapRefund(existingRefund, payment.RefundedAmountCents);
        }

        if (payment.Status is not (PaymentStatuses.Captured or PaymentStatuses.PartiallyRefunded))
        {
            throw new PaymentOperationException(
                $"Order {orderId} cannot be refunded because its payment state is {payment.Status}.",
                409);
        }

        if (string.IsNullOrWhiteSpace(payment.TransferId))
        {
            throw new PaymentOperationException(
                $"Order {orderId} has no captured Finix transfer to refund.",
                409);
        }

        var alreadyRefunded = await _db.RefundRecords
            .Where(record => record.OrderId == orderId)
            .SumAsync(record => (long?)record.AmountCents, cancellationToken) ?? 0;
        var refundable = payment.CapturedAmountCents - alreadyRefunded;
        if (amountCents > refundable)
        {
            throw new PaymentOperationException(
                $"The refund exceeds the refundable balance of {refundable} cents.",
                400);
        }

        var claim = await TryClaimRefundAsync(orderId, idempotencyKey, cancellationToken);
        if (claim is null)
        {
            var existing = await _db.RefundRecords.FirstOrDefaultAsync(
                record => record.OrderId == orderId && record.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                return MapRefund(existing, alreadyRefunded);
            }

            throw new PaymentOperationException(
                "A refund with this idempotency key is already in progress.",
                409);
        }

        try
        {
            FinixRefundResult refund;
            try
            {
                refund = await _finix.RefundAsync(
                    payment.TransferId!,
                    amountCents,
                    idempotencyKey,
                    cancellationToken);
            }
            catch (PaymentOperationException exception) when (exception.DuplicateIdempotency)
            {
                var reversals = await _finix.ListReversalsAsync(payment.TransferId!, cancellationToken);
                var existing = reversals.FirstOrDefault(reversal =>
                        string.Equals(reversal.IdempotencyId, idempotencyKey, StringComparison.Ordinal))
                    ?? reversals.FirstOrDefault(reversal => reversal.AmountCents == amountCents);
                if (existing is null)
                {
                    throw;
                }

                refund = new FinixRefundResult(
                    existing.Id,
                    existing.AmountCents ?? amountCents,
                    existing.State,
                    existing.ParentTransferId);
            }
            catch (PaymentOperationException exception) when (exception.OutcomeUnknown)
            {
                var reversals = await _finix.ListReversalsAsync(payment.TransferId!, cancellationToken);
                var recordedIds = await _db.RefundRecords
                    .Where(record => record.TransferId == payment.TransferId)
                    .Select(record => record.RefundTransferId)
                    .ToListAsync(cancellationToken);
                var match = reversals.FirstOrDefault(reversal =>
                    reversal.AmountCents == amountCents &&
                    !recordedIds.Contains(reversal.Id));
                if (match is null)
                {
                    throw;
                }

                refund = new FinixRefundResult(
                    match.Id,
                    match.AmountCents ?? amountCents,
                    match.State,
                    match.ParentTransferId);
            }

            var refundRecord = new RefundRecord
            {
                OrderId = orderId,
                BuyerId = buyerId,
                TransferId = payment.TransferId!,
                RefundTransferId = refund.RefundId,
                AmountCents = amountCents,
                State = refund.State ?? "PENDING",
                IdempotencyKey = idempotencyKey,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.RefundRecords.Add(refundRecord);

            payment.RefundedAmountCents = alreadyRefunded + amountCents;
            payment.Status = payment.RefundedAmountCents >= payment.CapturedAmountCents
                ? PaymentStatuses.Refunded
                : PaymentStatuses.PartiallyRefunded;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            order.MarkRefunded(payment.RefundedAmountCents >= payment.CapturedAmountCents);

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Order {OrderId} refunded {AmountCents} cents with refund {RefundId}; total refunded {RefundedCents}.",
                orderId,
                amountCents,
                refund.RefundId,
                payment.RefundedAmountCents);

            return MapRefund(refundRecord, payment.RefundedAmountCents);
        }
        catch (PaymentOperationException exception) when (exception.StatusCode < 500)
        {
            await ReleaseRefundClaimAsync(claim, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .Include(order => order.OrderItems)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(order => order.Id).ToArray();
        var payments = await _db.OrderPayments
            .Where(payment => orderIds.Contains(payment.OrderId))
            .ToDictionaryAsync(payment => payment.OrderId, cancellationToken);

        return orders
            .Select(order => MapOrder(order, payments.GetValueOrDefault(order.Id)))
            .ToList();
    }

    public async Task<SavedCardSummary> SaveCardAsync(
        string buyerId,
        CardInput card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);

        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
        var last4 = Last4(card.Number);
        var input = new FinixCardInput(
            card.Name,
            DigitsOnly(card.Number),
            card.ExpirationMonth,
            card.ExpirationYear,
            card.SecurityCode,
            card.Street,
            card.City,
            card.State,
            card.Country,
            card.ZipCode);

        FinixCardInfo saved;
        try
        {
            saved = await _finix.SaveCardAsync(identityId, input, cancellationToken);
        }
        catch (PaymentOperationException exception) when (exception.OutcomeUnknown)
        {
            var existing = await _finix.FindCardAsync(
                identityId,
                last4,
                card.ExpirationMonth,
                card.ExpirationYear,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            saved = existing;
        }

        var entity = new SavedCard
        {
            BuyerId = buyerId,
            FinixIdentityId = identityId,
            PaymentInstrumentId = saved.PaymentInstrumentId,
            Brand = saved.Brand,
            Last4 = saved.Last4 ?? last4,
            ExpirationMonth = saved.ExpirationMonth ?? card.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear ?? card.ExpirationYear,
            Fingerprint = saved.Fingerprint,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.SavedCards.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Saved card {PaymentMethodId} for {BuyerId} (brand {Brand}, last4 {Last4}).",
            entity.PaymentInstrumentId,
            buyerId,
            entity.Brand,
            entity.Last4);

        return MapSavedCard(entity);
    }

    public async Task<IReadOnlyList<SavedCardSummary>> ListCardsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var cards = await _db.SavedCards
            .Where(card => card.BuyerId == buyerId && !card.Removed)
            .OrderByDescending(card => card.CreatedAt)
            .ToListAsync(cancellationToken);

        return cards.Select(MapSavedCard).ToList();
    }

    public async Task DeleteCardAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
        {
            throw new PaymentOperationException("A payment method id is required.", 400);
        }

        var card = await _db.SavedCards.FirstOrDefaultAsync(
            candidate => candidate.BuyerId == buyerId &&
                         candidate.PaymentInstrumentId == paymentMethodId &&
                         !candidate.Removed,
            cancellationToken);

        if (card is null)
        {
            throw new PaymentOperationException("Saved card not found.", 404);
        }

        card.Removed = true;
        card.RemovedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Saved card {PaymentMethodId} removed for {BuyerId}.",
            paymentMethodId,
            buyerId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentOperationException("The reconciliation 'from' must not be after 'to'.", 400);
        }

        var provider = await _finix.ListTransactionsAsync(from, to, cancellationToken);

        var orders = await _db.Orders
            .Where(order => order.OrderDate >= from && order.OrderDate <= to)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(order => order.Id).ToArray();

        var payments = await _db.OrderPayments
            .Where(payment => orderIds.Contains(payment.OrderId))
            .ToListAsync(cancellationToken);
        var refunds = await _db.RefundRecords
            .Where(record => record.CreatedAt >= from && record.CreatedAt <= to)
            .ToListAsync(cancellationToken);

        var rows = new List<ReconciliationRow>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var transaction in provider.Items)
        {
            var refund = refunds.FirstOrDefault(record => record.RefundTransferId == transaction.Id);
            var payment = refund is null
                ? payments.FirstOrDefault(candidate => candidate.TransferId == transaction.Id)
                : null;
            var orderId = refund?.OrderId ?? payment?.OrderId;

            if (orderId is not null)
            {
                matchedOrderIds.Add(orderId.Value);
                rows.Add(new ReconciliationRow(
                    "MATCHED",
                    orderId,
                    transaction.Id,
                    transaction.Type,
                    transaction.State,
                    transaction.AmountCents,
                    transaction.FeeCents,
                    transaction.CreatedAt,
                    refund is null ? "Capture matched to order." : "Refund matched to order."));
            }
            else
            {
                rows.Add(new ReconciliationRow(
                    "PROVIDER_ONLY",
                    null,
                    transaction.Id,
                    transaction.Type,
                    transaction.State,
                    transaction.AmountCents,
                    transaction.FeeCents,
                    transaction.CreatedAt,
                    "Finix has this transaction but eShop has no matching order or refund."));
            }
        }

        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id))
            {
                continue;
            }

            var payment = payments.FirstOrDefault(candidate => candidate.OrderId == order.Id);
            var note = payment is null
                ? "eShop has this order but no payment record."
                : $"eShop payment state is {payment.Status}; Finix transfer {payment.TransferId ?? "(none)"} was not in the provider range.";

            rows.Add(new ReconciliationRow(
                "APP_ONLY",
                order.Id,
                payment?.TransferId,
                null,
                payment?.Status,
                payment?.OrderTotalCents,
                payment?.FeeCents,
                order.OrderDate,
                note));
        }

        return new ReconciliationReport(
            from,
            to,
            provider.Truncated,
            provider.NextCursor,
            provider.CoveredTo,
            provider.Items.Count,
            orders.Count,
            rows);
    }

    private async Task<(string PaymentInstrumentId, string IdentityId)> ResolvePaymentInstrumentAsync(
        string buyerId,
        PayOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.PaymentMethodId))
        {
            var saved = await _db.SavedCards.FirstOrDefaultAsync(
                card => card.BuyerId == buyerId &&
                        card.PaymentInstrumentId == command.PaymentMethodId &&
                        !card.Removed,
                cancellationToken);

            if (saved is null)
            {
                throw new PaymentOperationException("The saved card was not found.", 400);
            }

            if (string.IsNullOrWhiteSpace(saved.FinixIdentityId))
            {
                throw new PaymentOperationException("The saved card has no Finix identity.", 400);
            }

            var belongs = await _finix.CardBelongsToIdentityAsync(
                saved.FinixIdentityId,
                saved.PaymentInstrumentId,
                cancellationToken);
            if (!belongs)
            {
                throw new PaymentOperationException("The saved card is no longer available.", 400);
            }

            return (saved.PaymentInstrumentId, saved.FinixIdentityId);
        }

        if (command.Card is null)
        {
            throw new PaymentOperationException(
                "Provide either a saved paymentMethodId or one-off card details.",
                400);
        }

        ValidateCard(command.Card);

        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
        var input = new FinixCardInput(
            command.Card.Name,
            DigitsOnly(command.Card.Number),
            command.Card.ExpirationMonth,
            command.Card.ExpirationYear,
            command.Card.SecurityCode,
            command.Card.Street,
            command.Card.City,
            command.Card.State,
            command.Card.Country,
            command.Card.ZipCode);

        FinixCardInfo card;
        try
        {
            card = await _finix.SaveCardAsync(identityId, input, cancellationToken);
        }
        catch (PaymentOperationException exception) when (exception.OutcomeUnknown)
        {
            var existing = await _finix.FindCardAsync(
                identityId,
                Last4(command.Card.Number),
                command.Card.ExpirationMonth,
                command.Card.ExpirationYear,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            card = existing;
        }

        return (card.PaymentInstrumentId, identityId);
    }

    private async Task<string> GetOrCreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var profile = await _db.ShopperProfiles.FindAsync([buyerId], cancellationToken);
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.FinixIdentityId))
        {
            return profile.FinixIdentityId;
        }

        var email = buyerId.Contains("@", StringComparison.Ordinal) ? buyerId : null;
        string identityId;
        try
        {
            identityId = await _finix.EnsureShopperIdentityAsync(buyerId, email, cancellationToken);
        }
        catch (PaymentOperationException exception) when (exception.OutcomeUnknown && email is not null)
        {
            var found = await _finix.FindIdentityByEmailAsync(email, cancellationToken);
            if (found is null)
            {
                throw;
            }

            identityId = found;
        }

        if (profile is null)
        {
            profile = new ShopperProfile
            {
                BuyerId = buyerId,
                FinixIdentityId = identityId,
                Email = email,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.ShopperProfiles.Add(profile);
        }
        else
        {
            profile.FinixIdentityId = identityId;
            profile.Email = email;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return identityId;
    }

    private async Task ApplyAuthorizationAsync(
        Order order,
        OrderPayment payment,
        FinixAuthorizationResult authorization,
        string paymentInstrumentId,
        string? identityId,
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        payment.PaymentInstrumentId = paymentInstrumentId;
        payment.FinixIdentityId = identityId;
        payment.AuthorizationId = authorization.AuthorizationId;
        payment.AuthorizationState = authorization.State;
        payment.AuthorizedAmountCents = authorization.AmountCents > 0
            ? authorization.AmountCents
            : payment.OrderTotalCents;
        payment.IdempotencyId = idempotencyId;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        if (string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatuses.Failed;
            payment.FailureCode = authorization.FailureCode;
            payment.FailureMessage = authorization.FailureMessage;
            order.MarkPaymentFailed();
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                authorization.FailureMessage ?? "Finix declined the authorization.",
                402,
                authorization.FailureCode);
        }

        payment.Status = PaymentStatuses.Authorized;
        payment.FailureCode = null;
        payment.FailureMessage = null;
        order.MarkAuthorized();
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} authorized with Finix authorization {AuthorizationId} for {AmountCents} cents.",
            order.Id,
            authorization.AuthorizationId,
            payment.AuthorizedAmountCents);
    }

    private static void ApplyCapture(OrderPayment payment, Order order, FinixCaptureResult capture)
    {
        payment.AuthorizationState = capture.AuthorizationState ?? payment.AuthorizationState;
        payment.TransferId = capture.TransferId;
        payment.CapturedAmountCents = capture.CapturedAmountCents;
        payment.FeeCents = capture.FeeCents;
        payment.NetCents = capture.NetCents;
        payment.Status = PaymentStatuses.Captured;
        payment.FailureCode = null;
        payment.FailureMessage = null;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        order.MarkCaptured();
    }

    private async Task<string> DescribeHoldStateAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        var baseMessage =
            $"Order cannot be fulfilled because its payment state is {payment.Status}" +
            (string.IsNullOrWhiteSpace(payment.AuthorizationId)
                ? " and no Finix authorization is recorded."
                : $" (Finix authorization {payment.AuthorizationId}).");

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            return baseMessage;
        }

        var authorization = await TryGetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        if (authorization is null)
        {
            return baseMessage;
        }

        var detail = BuildCaptureFailureMessage(payment, authorization);
        return $"{baseMessage} {detail}";
    }

    private static string BuildCaptureFailureMessage(
        OrderPayment payment,
        FinixAuthorizationResult? authorization)
    {
        var state = authorization?.State ?? payment.AuthorizationState ?? "unknown";
        var voided = authorization?.IsVoid == true ? ", voided" : string.Empty;
        var expired = authorization?.ExpiresAt is { } expiresAt
            ? $", expired at {expiresAt:O}"
            : string.Empty;
        var reason = authorization?.FailureMessage ?? payment.FailureMessage;
        var reasonText = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Finix says: {reason}";
        var action = string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
            ? "The hold is still open; retry the capture."
            : "The hold can no longer be captured. Cancel the order, or contact Finix if the hold should still be open.";

        return $"Finix reports authorization state {state}{voided}{expired}.{reasonText} {action}";
    }

    private async Task<FinixAuthorizationResult?> TryGetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentOperationException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not read Finix authorization {AuthorizationId} while building an operator message.",
                authorizationId);
            return null;
        }
    }

    private async Task<FinixAuthorizationResult?> TryFindAuthorizationAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.FindAuthorizationByIdempotencyAsync(idempotencyId, cancellationToken);
        }
        catch (PaymentOperationException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not look up Finix authorization by idempotency id {IdempotencyId}.",
                idempotencyId);
            return null;
        }
    }

    private async Task<Order> LoadOrderForBuyerAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(candidate => candidate.OrderItems)
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException("Order not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(candidate => candidate.OrderItems)
            .FirstOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new PaymentOperationException("Order not found.", 404);
        }

        return order;
    }

    private async Task<OrderPayment> GetOrCreatePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await _db.OrderPayments
            .FirstOrDefaultAsync(candidate => candidate.OrderId == order.Id, cancellationToken);
        if (payment is not null)
        {
            return payment;
        }

        payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = PaymentStatuses.AwaitingPayment,
            OrderTotalCents = ToCents(order.Total()),
            IdempotencyId = $"eshop-order-{order.Id}-{Guid.NewGuid():N}-authorize",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    private async Task<OrderPayment> ReloadPaymentAsync(Order order, CancellationToken cancellationToken) =>
        await _db.OrderPayments.FirstOrDefaultAsync(
            candidate => candidate.OrderId == order.Id,
            cancellationToken) ?? throw new PaymentOperationException(
            $"No payment record exists for order {order.Id}.",
            409);

    private async Task<PaymentOperationClaim?> TryClaimAsync(
        int orderId,
        string operation,
        CancellationToken cancellationToken)
    {
        var claim = new PaymentOperationClaim
        {
            OrderId = orderId,
            Operation = operation,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            _db.PaymentOperationClaims.Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return claim;
        }
        catch (DbUpdateException)
        {
            Detach(claim);
            return null;
        }
        catch (ArgumentException)
        {
            Detach(claim);
            return null;
        }
        catch (InvalidOperationException)
        {
            Detach(claim);
            return null;
        }
    }

    private async Task ReleaseClaimAsync(PaymentOperationClaim claim, CancellationToken cancellationToken)
    {
        if (_db.Entry(claim).State != EntityState.Detached)
        {
            _db.PaymentOperationClaims.Remove(claim);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<RefundClaim?> TryClaimRefundAsync(
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var claim = new RefundClaim
        {
            OrderId = orderId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            _db.RefundClaims.Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return claim;
        }
        catch (DbUpdateException)
        {
            Detach(claim);
            return null;
        }
        catch (ArgumentException)
        {
            Detach(claim);
            return null;
        }
        catch (InvalidOperationException)
        {
            Detach(claim);
            return null;
        }
    }

    private async Task ReleaseRefundClaimAsync(RefundClaim claim, CancellationToken cancellationToken)
    {
        if (_db.Entry(claim).State != EntityState.Detached)
        {
            _db.RefundClaims.Remove(claim);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private void Detach(object entity)
    {
        var entry = _db.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static OrderSummary MapOrder(Order order, OrderPayment? payment)
    {
        var current = payment ?? new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Status = PaymentStatuses.AwaitingPayment,
            OrderTotalCents = ToCents(order.Total())
        };

        return new OrderSummary(
            order.Id,
            order.OrderDate,
            order.Total(),
            current.Status,
            new PaymentSummary(
                current.Status,
                current.PaymentInstrumentId,
                current.AuthorizationId,
                current.AuthorizationState,
                current.TransferId,
                current.AuthorizedAmountCents,
                current.CapturedAmountCents,
                current.FeeCents,
                current.NetCents,
                current.RefundedAmountCents,
                current.FailureCode,
                current.FailureMessage),
            order.OrderItems
                .Select(item => new OrderLineSummary(
                    item.ItemOrdered.CatalogItemId,
                    item.ItemOrdered.ProductName,
                    item.UnitPrice,
                    item.Units))
                .ToList());
    }

    private static RefundSummary MapRefund(RefundRecord record, long totalRefundedCents) =>
        new(
            record.RefundTransferId,
            record.OrderId,
            record.AmountCents,
            totalRefundedCents,
            record.State);

    private static SavedCardSummary MapSavedCard(SavedCard card) =>
        new(
            card.PaymentInstrumentId,
            card.Brand,
            card.Last4,
            card.ExpirationMonth,
            card.ExpirationYear);

    private static void ValidateAddress(ShippingAddressInput address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.State) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new PaymentOperationException(
                "A complete shipping address (street, city, state, country, zipCode) is required.",
                400);
        }
    }

    private static void ValidateCard(CardInput card)
    {
        if (card is null)
        {
            throw new PaymentOperationException("Card details are required.", 400);
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException("The cardholder name is required.", 400);
        }

        var digits = DigitsOnly(card.Number);
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentOperationException("The card number is not valid.", 400);
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException("The card expiration month must be between 1 and 12.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        if (card.ExpirationYear < now.Year ||
            (card.ExpirationYear == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentOperationException("The card has expired.", 400);
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.SecurityCode.Length is < 3 or > 4 ||
            !card.SecurityCode.All(char.IsDigit))
        {
            throw new PaymentOperationException("A 3- or 4-digit security code is required.", 400);
        }
    }

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static string Last4(string value)
    {
        var digits = DigitsOnly(value);
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static long ToCents(decimal amount)
    {
        var cents = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents <= 0 || cents > long.MaxValue)
        {
            throw new PaymentOperationException("The order total must be greater than zero.", 400);
        }

        return (long)cents;
    }
}
