using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinixAddress = FinixApiReference.Models.Address;
using OrderAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixPaymentService : IPaymentService
{
    private const string CardTagKey = "eshopBuyer";
    private readonly CatalogContext _db;
    private readonly FinixApiReferenceClient _finix;
    private readonly IOptions<FinixSettings> _settings;
    private readonly IUriComposer _uriComposer;
    private readonly KeyedAsyncLock _locks;
    private readonly ILogger<FinixPaymentService> _logger;

    public FinixPaymentService(CatalogContext db,
        FinixApiReferenceClient finix,
        IOptions<FinixSettings> settings,
        IUriComposer uriComposer,
        KeyedAsyncLock locks,
        ILogger<FinixPaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _settings = settings;
        _uriComposer = uriComposer;
        _locks = locks;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId,
        CreateOrderInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The caller could not be identified from the token.");
        }

        if (input.Items is null || input.Items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        foreach (var line in input.Items)
        {
            if (line.CatalogItemId <= 0)
            {
                throw new PaymentValidationException("Catalog item ids must be positive.");
            }

            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException("Item quantities must be greater than zero.");
            }
        }

        var itemIds = input.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(i => itemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in input.Items)
        {
            var catalogItem = catalogItems.First(i => i.Id == line.CatalogItemId);
            var ordered = new CatalogItemOrdered(catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, line.Quantity));
        }

        var shippingAddress = input.ShippingAddress ?? new OrderAddress("N/A", "N/A", "NA", "USA", "00000");
        var order = new Order(buyerId, shippingAddress, orderItems);

        var currency = CurrencyCode();
        var payment = new OrderPayment(currency, ToCents(order.Total()));
        order.AttachPayment(payment);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created order {OrderId} for {BuyerId} with payment awaiting authorization.",
            order.Id, buyerId);

        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The caller could not be identified from the token.");
        }

        return await _db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderPayment> PayOrderAsync(string buyerId,
        int orderId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await _db.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

        var amountCents = ToCents(order.Total());
        var payment = order.Payment;
        if (payment is null)
        {
            payment = new OrderPayment(CurrencyCode(), amountCents);
            order.AttachPayment(payment);
        }

        if (payment.Status is PaymentStatus.Authorized
            or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentConflictException($"Order {orderId} has been canceled and can no longer be paid.");
        }

        if (card is not null && savedPaymentMethodId.HasValue)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, not both.");
        }

        if (card is null && !savedPaymentMethodId.HasValue)
        {
            throw new PaymentValidationException("Provide card details or a saved paymentMethodId.");
        }

        string source;
        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _db.SavedCards
                .FirstOrDefaultAsync(c => c.Id == savedPaymentMethodId.Value && c.BuyerId == buyerId,
                    cancellationToken)
                ?? throw new PaymentNotFoundException(
                    $"Saved payment method {savedPaymentMethodId.Value} was not found.");

            source = savedCard.FinixPaymentInstrumentId;
        }
        else
        {
            var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
            var instrument = await CreateCardInstrumentAsync(identityId, card!, cancellationToken);
            source = instrument.Id!;
        }

        Authorization authorization;
        try
        {
            authorization = await _finix.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest
                {
                    Body = new AuthorizationAllFields
                    {
                        Amount = amountCents,
                        Currency = CurrencyEnum(),
                        Merchant = RequireMerchantId(),
                        Source = source,
                        IdempotencyId = string.IsNullOrWhiteSpace(payment.AuthorizationIdempotencyKey)
                            ? $"eshop-{orderId}-{payment.Id}"
                            : payment.AuthorizationIdempotencyKey,
                        Tags = new Dictionary<string, string>
                        {
                            ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture),
                            ["buyerId"] = buyerId
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            var detail = DescribeFinixException(ex);
            payment.RecordAuthorizationFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentGatewayException($"Finix could not authorize order {orderId}: {detail}");
        }

        if (authorization.State != State1.Succeeded || authorization.IsVoid == true)
        {
            var detail = DescribeAuthorizationFailure(authorization);
            payment.RecordAuthorizationFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentGatewayException($"Finix declined authorization for order {orderId}: {detail}");
        }

        if (authorization.Id is null)
        {
            throw new PaymentGatewayException("Finix authorized the payment but did not return an authorization id.");
        }

        if (authorization.Amount.HasValue && authorization.Amount.Value != amountCents)
        {
            await TryVoidAuthorizationAsync(authorization.Id, cancellationToken);
            var detail = $"Finix authorized {authorization.Amount.Value} cents, expected {amountCents} cents.";
            payment.RecordAuthorizationFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentGatewayException(detail);
        }

        payment.RecordAuthorization(authorization.Id,
            authorization.State?.Value ?? State1.Succeeded.Value,
            authorization.Amount ?? amountCents,
            source);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Authorized order {OrderId}; authorization {AuthorizationId}, amount {AmountCents} cents.",
            orderId, authorization.Id, amountCents);

        return payment;
    }

    public async Task<OrderPayment> FulfilOrderAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await _db.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

        var payment = order.Payment
            ?? throw new PaymentConflictException($"Order {orderId} has no payment to fulfil.");

        if (payment.Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentConflictException($"Order {orderId} was canceled and cannot be fulfilled.");
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentConflictException(
                $"Order {orderId} is not awaiting fulfilment because it has no successful authorization. Pay for the order first.");
        }

        Authorization authorization;
        try
        {
            authorization = await _finix.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = payment.FinixAuthorizationId },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            var detail = DescribeFinixException(ex);
            payment.RecordCaptureFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentGatewayException(
                $"Could not read Finix authorization {payment.FinixAuthorizationId}: {detail}");
        }

        if (authorization.State != State1.Succeeded || authorization.IsVoid == true)
        {
            var detail = DescribeAuthorizationFailure(authorization);
            payment.RecordCaptureFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentConflictException(
                $"Authorization {payment.FinixAuthorizationId} can no longer be captured ({detail}). " +
                "Cancel the order or ask the shopper to pay again.");
        }

        Authorization capture;
        var captureAmount = payment.AuthorizedAmountCents > 0
            ? payment.AuthorizedAmountCents
            : ToCents(order.Total());

        if (captureAmount <= 0)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has no authorized amount to capture. Pay for the order again.");
        }

        try
        {
            capture = await _finix.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = payment.FinixAuthorizationId,
                    Body = new CaptureAuthorization
                    {
                        CaptureAmount = captureAmount
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            var detail = DescribeFinixException(ex);
            payment.RecordCaptureFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentConflictException(
                $"Finix could not capture authorization {payment.FinixAuthorizationId}: {detail}. " +
                "The hold may have expired or been voided; cancel the order or ask the shopper to pay again.");
        }

        var transferId = capture.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            // Some Finix responses omit the transfer id; read the authorization again.
            try
            {
                var refreshed = await _finix.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = payment.FinixAuthorizationId },
                    cancellationToken: cancellationToken);
                transferId = refreshed.Transfer;
            }
            catch (ApiException)
            {
                // The capture may still have succeeded; fall through to the explicit error below.
            }
        }

        if (string.IsNullOrWhiteSpace(transferId))
        {
            payment.RecordCaptureFailure("Finix captured the authorization but returned no transfer id.");
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentGatewayException(
                "Finix captured the authorization but did not return a transfer id, so the capture cannot be reconciled.");
        }

        Transfer transfer;
        try
        {
            transfer = await WaitForTransferToSettleAsync(transferId, cancellationToken);
        }
        catch (ApiException ex)
        {
            var detail = DescribeFinixException(ex);
            payment.RecordCaptureFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentGatewayException(
                $"Finix captured authorization {payment.FinixAuthorizationId} as transfer {transferId}, " +
                $"but the transfer could not be read: {detail}");
        }

        if (transfer.State == State11.Failed)
        {
            var detail = $"{transfer.FailureCode ?? "FAILED"}: {transfer.FailureMessage ?? "Finix reported the capture as failed."}";
            payment.RecordCaptureFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentConflictException($"Finix reported capture {transferId} as failed ({detail}).");
        }

        var capturedAmount = transfer.Amount ?? captureAmount;
        var fee = transfer.Fee ?? 0;
        var state = transfer.State?.Value ?? capture.State?.Value ?? "UNKNOWN";

        payment.RecordCapture(transferId, state, capturedAmount, fee);
        await _db.SaveChangesAsync(cancellationToken);

        await TryTagTransferAsync(transferId, orderId, order.BuyerId, cancellationToken);

        _logger.LogInformation(
            "Captured order {OrderId}; transfer {TransferId}, amount {AmountCents} cents, fee {FeeCents} cents, net {NetCents} cents.",
            orderId, transferId, capturedAmount, fee, payment.NetProceedsCents);

        return payment;
    }

    public async Task<OrderPayment> CancelOrderAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await _db.Orders
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

        var payment = order.Payment
            ?? throw new PaymentConflictException($"Order {orderId} has no payment to cancel.");

        if (payment.Status == PaymentStatus.Canceled)
        {
            return payment;
        }

        if (payment.Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has already been captured and cannot be canceled. Use the refunds endpoint instead.");
        }

        if (payment.Status == PaymentStatus.AwaitingPayment || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            payment.RecordCancel("NOT_AUTHORIZED");
            await _db.SaveChangesAsync(cancellationToken);
            return payment;
        }

        try
        {
            var voided = await _finix.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = payment.FinixAuthorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: cancellationToken);

            payment.RecordCancel(voided.VoidState?.Value
                ?? (voided.IsVoid == true ? "CANCELED" : voided.State?.Value ?? "CANCELED"));
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Canceled order {OrderId}; voided authorization {AuthorizationId}.",
                orderId, payment.FinixAuthorizationId);

            return payment;
        }
        catch (ApiException ex)
        {
            var detail = DescribeFinixException(ex);

            // A repeated cancel after a successful void is still idempotent.
            try
            {
                var current = await _finix.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = payment.FinixAuthorizationId },
                    cancellationToken: cancellationToken);

                if (current.IsVoid == true || current.State == State1.Canceled)
                {
                    payment.RecordCancel(current.VoidState?.Value
                        ?? (current.IsVoid == true ? "CANCELED" : current.State?.Value ?? "CANCELED"));
                    await _db.SaveChangesAsync(cancellationToken);
                    return payment;
                }
            }
            catch (ApiException)
            {
                // Fall through to the actionable conflict below.
            }

            payment.RecordCaptureFailure(detail);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentConflictException(
                $"Finix could not void authorization {payment.FinixAuthorizationId}: {detail}. " +
                "The hold may already have been captured or expired.");
        }
    }

    public async Task<RefundResult> RefundOrderAsync(int orderId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("An idempotencyKey is required for refunds.");
        }

        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The refund amount must be greater than zero.");
        }

        if (amountCents > int.MaxValue)
        {
            throw new PaymentValidationException("The refund amount is too large.");
        }

        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await _db.Orders
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

        var payment = order.Payment
            ?? throw new PaymentConflictException($"Order {orderId} has no payment to refund.");

        if (payment.Status is not (PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded))
        {
            throw new PaymentConflictException(
                $"Order {orderId} has not been captured, so there are no funds to refund.");
        }

        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            throw new PaymentConflictException($"Order {orderId} has no Finix capture transfer to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

        if (existing is not null)
        {
            if (existing.AmountCents != amountCents)
            {
                throw new PaymentConflictException(
                    $"Idempotency key '{idempotencyKey}' was already used for a refund of {existing.AmountCents} cents.");
            }

            return new RefundResult(existing, payment);
        }

        var refundable = payment.RefundableAmountCents();
        if (amountCents > refundable)
        {
            throw new PaymentConflictException(
                $"Refund of {amountCents} cents exceeds the remaining refundable amount of {refundable} cents.");
        }

        var reversal = await CreateRefundReversalAsync(payment.FinixCaptureTransferId,
            amountCents,
            cancellationToken);

        if (reversal.State == State11.Failed)
        {
            var detail = $"{reversal.FailureCode ?? "FAILED"}: {reversal.FailureMessage ?? "Finix reported the refund as failed."}";
            throw new PaymentGatewayException($"Finix rejected refund for order {orderId}: {detail}");
        }

        if (string.IsNullOrWhiteSpace(reversal.Id))
        {
            throw new PaymentGatewayException("Finix created a refund but did not return a transfer id.");
        }

        var refund = new PaymentRefund(payment.Id,
            idempotencyKey,
            amountCents,
            reversal.Id,
            reversal.State?.Value ?? "UNKNOWN");

        payment.AddRefund(refund);
        _db.PaymentRefunds.Add(refund);

        var newRefundedTotal = payment.RefundedAmountCents + amountCents;
        payment.UpdateRefundedTotal(newRefundedTotal);

        await _db.SaveChangesAsync(cancellationToken);

        await TryTagTransferAsync(reversal.Id, orderId, order.BuyerId, cancellationToken);

        _logger.LogInformation("Refunded order {OrderId}; refund {RefundId}, Finix transfer {TransferId}, amount {AmountCents} cents.",
            orderId, refund.Id, reversal.Id, amountCents);

        return new RefundResult(refund, payment);
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The caller could not be identified from the token.");
        }

        using var handle = await _locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);

        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
        var instrument = await CreateCardInstrumentAsync(identityId, card, cancellationToken);

        var savedCard = new SavedCard(buyerId,
            instrument.Id!,
            instrument.Brand?.Value,
            instrument.LastFour,
            instrument.ExpirationMonth ?? ResolveExpiration(card).Month,
            instrument.ExpirationYear ?? ResolveExpiration(card).Year,
            instrument.Name ?? card.Name);

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved card {PaymentMethodId} for {BuyerId}; Finix instrument {InstrumentId}.",
            savedCard.Id, buyerId, instrument.Id);

        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The caller could not be identified from the token.");
        }

        return await _db.SavedCards
            .AsNoTracking()
            .Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        using var handle = await _locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);

        var savedCard = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.Id == paymentMethodId && c.BuyerId == buyerId, cancellationToken)
            ?? throw new PaymentNotFoundException($"Saved payment method {paymentMethodId} was not found.");

        try
        {
            // Do not disable an instrument that is still backing an open authorization, otherwise
            // the operator could no longer capture an order the shopper has already paid for.
            var hasOpenAuthorization = await _db.OrderPayments.AnyAsync(
                p => p.PaymentInstrumentId == savedCard.FinixPaymentInstrumentId
                    && p.Status == PaymentStatus.Authorized,
                cancellationToken);

            if (!hasOpenAuthorization)
            {
                await _finix.PaymentInstrumentsApi.UpdatePaymentInstrument(
                    new UpdatePaymentInstrumentRequest
                    {
                        PaymentInstrumentId = savedCard.FinixPaymentInstrumentId,
                        Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                            new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                    },
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // If Finix no longer knows the instrument there is nothing left to disable; the local
            // record is still removed so it cannot be used by this application again.
            _logger.LogWarning("Finix could not disable payment instrument {InstrumentId}: {Detail}",
                savedCard.FinixPaymentInstrumentId, DescribeFinixException(ex));
        }

        _db.SavedCards.Remove(savedCard);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentValidationException("The from date must be earlier than or equal to the to date.");
        }

        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .ToListAsync(cancellationToken);

        var orderById = orders.ToDictionary(o => o.Id);
        var transferToOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        var authorizationToOrder = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var order in orders)
        {
            if (order.Payment is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(order.Payment.FinixAuthorizationId))
            {
                authorizationToOrder[order.Payment.FinixAuthorizationId] = order.Id;
            }

            if (!string.IsNullOrWhiteSpace(order.Payment.FinixCaptureTransferId))
            {
                transferToOrder[order.Payment.FinixCaptureTransferId] = order.Id;
            }

            foreach (var refund in order.Payment.Refunds)
            {
                if (!string.IsNullOrWhiteSpace(refund.FinixTransferId))
                {
                    transferToOrder[refund.FinixTransferId] = order.Id;
                }
            }
        }

        var finixTransactions = new List<ReconciliationFinixTransaction>();

        foreach (var authorization in await ListAuthorizationsAsync(from, to, cancellationToken))
        {
            var orderId = ResolveOrderId(authorization.Tags, authorization.Id, authorizationToOrder, transferToOrder);
            finixTransactions.Add(new ReconciliationFinixTransaction
            {
                Id = authorization.Id ?? string.Empty,
                Kind = "AUTHORIZATION",
                Type = "AUTHORIZATION",
                State = authorization.State?.Value,
                AmountCents = authorization.Amount ?? 0,
                Currency = authorization.Currency?.Value,
                CreatedAt = authorization.CreatedAt,
                OrderId = orderId,
                Matched = orderId.HasValue && orderById.ContainsKey(orderId.Value)
            });
        }

        foreach (var transfer in await ListTransfersAsync(from, to, cancellationToken))
        {
            var orderId = ResolveOrderId(transfer.Tags, transfer.Id, authorizationToOrder, transferToOrder);
            finixTransactions.Add(new ReconciliationFinixTransaction
            {
                Id = transfer.Id ?? string.Empty,
                Kind = "TRANSFER",
                Type = transfer.Type?.Value,
                State = transfer.State?.Value,
                AmountCents = transfer.Amount ?? 0,
                FeeCents = transfer.Fee,
                Currency = transfer.Currency?.Value,
                CreatedAt = transfer.CreatedAt,
                ParentTransferId = transfer.ParentTransfer,
                OrderId = orderId,
                Matched = orderId.HasValue && orderById.ContainsKey(orderId.Value)
            });
        }

        finixTransactions = finixTransactions
            .OrderBy(t => t.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        var ordersInRange = orders
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .OrderBy(o => o.OrderDate)
            .Select(order =>
            {
                var payment = order.Payment;
                var refundIds = payment?.Refunds.Select(r => r.FinixTransferId).ToArray()
                    ?? Array.Empty<string>();

                var matched = finixTransactions.Any(t =>
                    t.OrderId == order.Id
                    || (payment?.FinixAuthorizationId is not null && t.Id == payment.FinixAuthorizationId)
                    || (payment?.FinixCaptureTransferId is not null && t.Id == payment.FinixCaptureTransferId)
                    || refundIds.Contains(t.Id));

                return new ReconciliationOrder
                {
                    OrderId = order.Id,
                    OrderDate = order.OrderDate,
                    AmountCents = payment?.AmountCents ?? ToCents(order.Total()),
                    Currency = payment?.Currency ?? CurrencyCode(),
                    PaymentStatus = payment?.Status.ToString() ?? "NoPayment",
                    FinixAuthorizationId = payment?.FinixAuthorizationId,
                    FinixCaptureTransferId = payment?.FinixCaptureTransferId,
                    FinixRefundTransferIds = refundIds,
                    Matched = matched
                };
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FinixTransactions = finixTransactions,
            EShopOrders = ordersInRange,
            UnmatchedFinixTransactionIds = finixTransactions
                .Where(t => !t.Matched)
                .Select(t => t.Id)
                .ToArray(),
            UnmatchedOrderIds = ordersInRange
                .Where(o => !o.Matched)
                .Select(o => o.OrderId)
                .ToArray()
        };
    }

    private async Task<List<Authorization>> ListAuthorizationsAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<Authorization>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        while (true)
        {
            // The SDK's created_at filters serialize with fractional seconds, which the Finix
            // sandbox rejects. Page through Finix's records and apply the range locally instead.
            var page = await _finix.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    Limit = 100,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            var items = page.Embedded?.Authorizations;
            if (items is { Count: > 0 })
            {
                results.AddRange(items);
            }

            var next = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || !seenCursors.Add(next))
            {
                break;
            }

            cursor = next;
        }

        return results
            .Where(a => a.CreatedAt is null
                || (a.CreatedAt.Value >= from && a.CreatedAt.Value <= to))
            .ToList();
    }

    private async Task<List<Transfer>> ListTransfersAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<Transfer>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        while (true)
        {
            // See the authorizations listing above: filter the range locally because the SDK's
            // created_at query format is rejected by the sandbox.
            var page = await _finix.TransfersApi.ListTransfers(
                new ListTransfersRequest
                {
                    Limit = 100,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            var items = page.Embedded?.Transfers;
            if (items is { Count: > 0 })
            {
                results.AddRange(items);
            }

            var next = page.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || !seenCursors.Add(next))
            {
                break;
            }

            cursor = next;
        }

        return results
            .Where(t => t.CreatedAt is null
                || (t.CreatedAt.Value >= from && t.CreatedAt.Value <= to))
            .ToList();
    }

    private async Task<string> GetOrCreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        using var identityHandle = await _locks.AcquireAsync($"identity:{buyerId}", cancellationToken);

        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);

        if (profile?.FinixIdentityId is { Length: > 0 } existing)
        {
            return existing;
        }

        FinixApiReference.Models.Identity identity;
        try
        {
            identity = await _finix.Identities.CreateIdentity(
                new CreateIdentityRequest
                {
                    Body = new Buyer
                    {
                        Entity = new Entity5
                        {
                            Email = buyerId,
                            FirstName = "eShop",
                            LastName = "Shopper"
                        },
                        IdentityRoles = new[] { IdentityRole5.Buyer },
                        Type = Type10.Personal,
                        Tags = new Dictionary<string, string> { [CardTagKey] = buyerId }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(
                $"Finix could not create a buyer identity for {buyerId}: {DescribeFinixException(ex)}");
        }

        if (string.IsNullOrWhiteSpace(identity.Id))
        {
            throw new PaymentGatewayException("Finix created an identity but did not return an identity id.");
        }

        if (profile is null)
        {
            profile = new CustomerProfile(buyerId);
            _db.CustomerProfiles.Add(profile);
        }

        profile.SetFinixIdentityId(identity.Id);
        await _db.SaveChangesAsync(cancellationToken);

        return identity.Id;
    }

    private async Task<PaymentInstrumentCard> CreateCardInstrumentAsync(string identityId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        var (month, year) = ResolveExpiration(card);
        var number = NormalizeCardNumber(card.Number);

        if (string.IsNullOrWhiteSpace(number))
        {
            throw new PaymentValidationException("A card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("The cardholder name is required.");
        }

        if (month is < 1 or > 12)
        {
            throw new PaymentValidationException("The expiration month must be between 1 and 12.");
        }

        if (year < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentValidationException("The card expiration year is in the past.");
        }

        CreatePaymentInstrument response;
        try
        {
            response = await _finix.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest
                {
                    Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(
                        new PaymentInstrumentCard1
                        {
                            Identity = identityId,
                            Name = card.Name.Trim(),
                            Number = number,
                            ExpirationMonth = month,
                            ExpirationYear = year,
                            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode)
                                ? null
                                : card.SecurityCode.Trim(),
                            Address = MapBillingAddress(card.BillingAddress)
                        })
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            throw new PaymentGatewayException(
                $"Finix could not tokenize the card: {DescribeFinixException(ex)}");
        }

        if (!response.TryGetPaymentInstrumentCard(out var instrument) || string.IsNullOrWhiteSpace(instrument?.Id))
        {
            throw new PaymentGatewayException("Finix tokenized the card but did not return a payment instrument id.");
        }

        return instrument!;
    }

    private async Task<Transfer> CreateRefundReversalAsync(string transferId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.TransfersApi.CreateTransferReversal(
                new CreateTransferReversalRequest
                {
                    TransferId = transferId,
                    Body = new RefundTransfer { RefundAmount = (int)amountCents }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            var detail = DescribeFinixException(ex);
            if (!detail.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentGatewayException(
                    $"Finix could not refund capture {transferId}: {detail}");
            }

            // A partial refund is only allowed once the capture transfer has succeeded.
            var settled = await WaitForTransferToSettleAsync(transferId, cancellationToken);
            if (settled.State == State11.Pending)
            {
                throw new PaymentConflictException(
                    $"Finix capture {transferId} is still PENDING. Try the refund again once the capture has succeeded.");
            }

            try
            {
                return await _finix.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = transferId,
                        Body = new RefundTransfer { RefundAmount = (int)amountCents }
                    },
                    cancellationToken: cancellationToken);
            }
            catch (ApiException retryException)
            {
                throw new PaymentGatewayException(
                    $"Finix could not refund capture {transferId}: {DescribeFinixException(retryException)}");
            }
        }
    }

    private async Task<Transfer> WaitForTransferToSettleAsync(string transferId, CancellationToken cancellationToken)
    {
        var transfer = await _finix.TransfersApi.GetTransfer(
            new GetTransferRequest { TransferId = transferId },
            cancellationToken: cancellationToken);

        // Finix sandbox often returns a freshly captured transfer as PENDING for a moment.
        // Waiting briefly lets the capture report its final state (and permits partial refunds).
        // In the sandbox this transition can take ~15 seconds.
        for (var attempt = 0; attempt < 30 && transfer.State == State11.Pending; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            transfer = await _finix.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: cancellationToken);
        }

        return transfer;
    }

    private async Task TryVoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            await _finix.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = authorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not void authorization {AuthorizationId}: {Detail}",
                authorizationId, DescribeFinixException(ex));
        }
    }

    private async Task TryTagTransferAsync(string transferId,
        int orderId,
        string buyerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _finix.TransfersApi.UpdateTransfer(
                new UpdateTransferRequest
                {
                    TransferId = transferId,
                    Body = new UpdateTransfer
                    {
                        Tags = new Dictionary<string, string>
                        {
                            ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture),
                            ["buyerId"] = buyerId
                        }
                    }
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not tag Finix transfer {TransferId} with order {OrderId}: {Detail}",
                transferId, orderId, DescribeFinixException(ex));
        }
    }

    private string RequireMerchantId()
    {
        var merchantId = _settings.Value.MerchantId;
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            throw new PaymentConfigurationException(
                "Finix:MerchantId is not configured. Set FINIX_MERCHANT_ID or the Finix:MerchantId user-secret.");
        }

        return merchantId;
    }

    private string CurrencyCode()
    {
        var currency = _settings.Value.Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new PaymentConfigurationException(
                "Finix:Currency is not configured. Set FINIX_CURRENCY or the Finix:Currency user-secret.");
        }

        return currency.Trim().ToUpperInvariant();
    }

    private Currency CurrencyEnum()
    {
        var code = CurrencyCode();
        return JsonSerializer.Deserialize<Currency>(JsonSerializer.Serialize(code))
            ?? throw new PaymentConfigurationException($"Finix:Currency '{code}' is not a valid currency code.");
    }

    private static long ToCents(decimal amount)
    {
        return checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeCardNumber(string? number)
    {
        return (number ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static (int Month, int Year) ResolveExpiration(CardDetails card)
    {
        if (card.ExpirationMonth.HasValue && card.ExpirationYear.HasValue)
        {
            return (card.ExpirationMonth.Value, NormalizeYear(card.ExpirationYear.Value));
        }

        if (!string.IsNullOrWhiteSpace(card.Expiration))
        {
            var parts = card.Expiration.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                return (month, NormalizeYear(year));
            }
        }

        throw new PaymentValidationException(
            "Card expiration is required. Provide expiration as MM/YYYY or expirationMonth and expirationYear.");
    }

    private static int NormalizeYear(int year)
    {
        return year < 100 ? 2000 + year : year;
    }

    private static FinixAddress? MapBillingAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(address.Line1)
            || string.IsNullOrWhiteSpace(address.City)
            || string.IsNullOrWhiteSpace(address.Region)
            || string.IsNullOrWhiteSpace(address.PostalCode))
        {
            throw new PaymentValidationException(
                "A billing address must include line1, city, region and postalCode.");
        }

        var countryCode = string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country.Trim().ToUpperInvariant();
        var country = JsonSerializer.Deserialize<Country>(JsonSerializer.Serialize(countryCode))
            ?? Country.Usa;

        return new FinixAddress
        {
            Line1 = address.Line1.Trim(),
            Line2 = string.IsNullOrWhiteSpace(address.Line2) ? null : address.Line2.Trim(),
            City = address.City.Trim(),
            Region = address.Region.Trim(),
            PostalCode = address.PostalCode.Trim(),
            Country = country
        };
    }

    private static int? ResolveOrderId(object? tags,
        string? resourceId,
        IReadOnlyDictionary<string, int> authorizationToOrder,
        IReadOnlyDictionary<string, int> transferToOrder)
    {
        var tagged = ReadTag(tags, "orderId", "order_id");
        if (tagged is not null
            && int.TryParse(tagged, NumberStyles.Integer, CultureInfo.InvariantCulture, out var taggedOrderId))
        {
            return taggedOrderId;
        }

        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            if (authorizationToOrder.TryGetValue(resourceId, out var authOrderId))
            {
                return authOrderId;
            }

            if (transferToOrder.TryGetValue(resourceId, out var transferOrderId))
            {
                return transferOrderId;
            }
        }

        return null;
    }

    private static string? ReadTag(object? tags, params string[] keys)
    {
        if (tags is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in keys)
            {
                if (element.TryGetProperty(key, out var value))
                {
                    return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                }
            }
        }

        if (tags is IReadOnlyDictionary<string, string> stringDictionary)
        {
            foreach (var key in keys)
            {
                if (stringDictionary.TryGetValue(key, out var value))
                {
                    return value;
                }
            }
        }

        if (tags is IDictionary<string, object> dictionary)
        {
            foreach (var key in keys)
            {
                if (dictionary.TryGetValue(key, out var value))
                {
                    return value?.ToString();
                }
            }
        }

        return null;
    }

    private static string DescribeAuthorizationFailure(Authorization authorization)
    {
        var state = authorization.State?.Value ?? "UNKNOWN";
        var parts = new List<string> { $"state {state}" };
        if (authorization.IsVoid == true)
        {
            parts.Add("voided");
        }

        if (!string.IsNullOrWhiteSpace(authorization.FailureCode))
        {
            parts.Add($"code {authorization.FailureCode}");
        }

        if (!string.IsNullOrWhiteSpace(authorization.FailureMessage))
        {
            parts.Add(authorization.FailureMessage);
        }

        return string.Join(", ", parts);
    }

    private static string DescribeFinixException(Exception exception)
    {
        if (exception is not ApiException apiException)
        {
            return exception.Message;
        }

        var error = exception.GetType().GetProperty("Error")?.GetValue(exception);
        if (error is ApiError apiError)
        {
            if (apiError.TryGetRawError(out var rawError))
            {
                var body = rawError.ReadAsString();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    return body;
                }
            }

            var tryGetGeneric = error.GetType().GetMethod("TryGetGenericError");
            if (tryGetGeneric is not null)
            {
                var arguments = new object?[] { null };
                if (tryGetGeneric.Invoke(error, arguments) is true
                    && arguments[0] is GenericError genericError
                    && genericError.Embedded?.Errors is { Count: > 0 } errors)
                {
                    return string.Join("; ", errors.Select(e =>
                        string.IsNullOrWhiteSpace(e.Code) ? e.Message : $"{e.Code}: {e.Message}"));
                }
            }
        }

        return $"{apiException.Message} (HTTP {(int)apiException.StatusCode})";
    }
}
