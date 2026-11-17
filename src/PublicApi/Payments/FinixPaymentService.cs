using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Errors;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using FinixAddress = FinixApiReference.Models.Address;
using FinixCurrency = FinixApiReference.Models.Enums.Currency;
using Authorization = FinixApiReference.Models.Authorization;
using FinixIdentity = FinixApiReference.Models.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Owns every Finix interaction and the application-side payment state that makes later calls possible.
/// Full card details pass through this class only on their way to Finix; they are never persisted or logged.
/// </summary>
public class FinixPaymentService
{
    private const int MaxPages = 1000;
    private const int PageSize = 100;

    private readonly FinixApiReferenceClient _client;
    private readonly CatalogContext _db;
    private readonly FinixOptions _finix;
    private readonly ILogger<FinixPaymentService> _logger;

    public FinixPaymentService(
        FinixApiReferenceClient client,
        CatalogContext db,
        IOptions<FinixOptions> finix,
        ILogger<FinixPaymentService> logger)
    {
        _client = client;
        _db = db;
        _finix = finix.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- orders

    public async Task<CreateOrderResponse> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentException(400, "At least one order item is required.");
        }

        foreach (var line in request.Items)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new PaymentException(400, "Each order item needs a positive catalogItemId and quantity.");
            }
        }

        PaymentOperation? claim = null;
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var claimKey = $"order:{buyerId}:{request.IdempotencyKey}";
            var claimed = await TryClaimAsync(claimKey, "order", buyerId, null, cancellationToken);
            if (!claimed.IsNew)
            {
                if (claimed.Operation.State == PaymentOperation.Completed
                    && int.TryParse(claimed.Operation.ResultId, out var existingId))
                {
                    var existingPayment = await _db.OrderPayments.FindAsync(new object?[] { existingId }, cancellationToken);
                    if (existingPayment is not null)
                    {
                        return ToCreateOrderResponse(existingId, existingPayment);
                    }
                }

                throw new PaymentException(409, "A request with this idempotency key is already in progress.");
            }

            claim = claimed.Operation;
        }

        var itemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        var catalogItems = await _db.CatalogItems
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var missing = itemIds.Where(id => !catalogItems.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            var catalogItem = catalogItems[line.CatalogItemId];
            var picture = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : catalogItem.PictureUri;
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture),
                catalogItem.Price,
                line.Quantity));
        }

        var order = new Order(buyerId, MapShippingAddress(request.ShipToAddress), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var amountCents = ToCents(order.Total());
        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = buyerId,
            ProviderKey = Guid.NewGuid().ToString("N"),
            Currency = NormalizedCurrency,
            AmountCents = amountCents,
            Status = OrderPayment.AwaitingPayment,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        if (claim is not null)
        {
            await CompleteClaimAsync(claim, order.Id.ToString(), cancellationToken);
        }

        _logger.LogInformation("Created order {OrderId} for {BuyerId} with {AmountCents} {Currency}",
            order.Id, buyerId, amountCents, NormalizedCurrency);

        return ToCreateOrderResponse(order.Id, payment);
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .Where(p => orderIds.Contains(p.OrderId))
            .ToDictionaryAsync(p => p.OrderId, cancellationToken);

        var response = new MyOrdersResponse();
        foreach (var order in orders)
        {
            payments.TryGetValue(order.Id, out var payment);
            response.Orders.Add(ToOrderSummary(order, payment));
        }

        return response;
    }

    // ------------------------------------------------------------ pay / hold

    public async Task<PayOrderResponse> PayOrderAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (_, payment) = await LoadOrderAsync(orderId, buyerId, cancellationToken);

        if (payment.Status is OrderPayment.Authorized or OrderPayment.Captured
            or OrderPayment.PartiallyRefunded or OrderPayment.Refunded)
        {
            return ToPayResponse(payment);
        }

        if (payment.Status == OrderPayment.Canceled)
        {
            throw new PaymentException(409, "This order was canceled; the authorization was released.");
        }

        var claimKey = $"pay:{payment.ProviderKey}";
        var claimed = await TryClaimAsync(claimKey, "pay", buyerId, orderId, cancellationToken);
        if (!claimed.IsNew)
        {
            if (claimed.Operation.State == PaymentOperation.Completed)
            {
                var refreshed = await LoadPaymentAsync(orderId, cancellationToken);
                if (refreshed.Status is OrderPayment.Authorized or OrderPayment.Captured)
                {
                    return ToPayResponse(refreshed);
                }
            }

            if (claimed.Operation.State is PaymentOperation.Claimed or PaymentOperation.Unknown)
            {
                var reconciled = await TryReconcileAuthorizationAsync(orderId, claimKey, cancellationToken);
                if (reconciled is not null)
                {
                    await CompleteClaimAsync(claimed.Operation, reconciled.Id ?? string.Empty, cancellationToken);
                    return ToPayResponse(payment);
                }
            }

            throw new PaymentException(409, "A payment for this order is already in progress or its outcome is unknown.");
        }

        var claim = claimed.Operation;

        string source;
        if (!string.IsNullOrWhiteSpace(request.PaymentMethodId))
        {
            source = await ResolveSavedCardSourceAsync(buyerId, request.PaymentMethodId!, cancellationToken);
        }
        else if (request.Card is not null)
        {
            ValidateCard(request.Card);
            var identityId = await GetOrCreateIdentityIdAsync(buyerId, cancellationToken);
            var instrument = await CreateCardInstrumentAsync(identityId, request.Card, "oneoff", buyerId, cancellationToken);
            source = instrument.Id!;
        }
        else
        {
            throw new PaymentException(400, "Supply either card details or a saved paymentMethodId.");
        }

        var allFields = new AuthorizationAllFields
        {
            Amount = payment.AmountCents,
            Currency = MapCurrency(),
            Merchant = _finix.MerchantId,
            Source = source,
            IdempotencyId = claimKey,
            SecurityCode = request.Card?.SecurityCode,
            Tags = new Dictionary<string, string>
            {
                ["orderId"] = orderId.ToString(),
                ["buyerId"] = buyerId
            }
        };

        Authorization? authorization;
        try
        {
            authorization = await _client.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest
                {
                    Body = AuthorizationsRequest.AuthorizationAllFields(allFields)
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            var reconciled = await TryReconcileAuthorizationAsync(orderId, claimKey, cancellationToken);
            if (reconciled is not null)
            {
                await CompleteClaimAsync(claim, reconciled.Id ?? string.Empty, cancellationToken);
                return ToPayResponse(payment);
            }

            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            await ReleaseClaimAsync(claim, cancellationToken);
            await RecordPaymentFailureAsync(payment, message, cancellationToken);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            await MarkClaimUnknownAsync(claim, "Unreadable authorization response.", cancellationToken);
            throw new PaymentException(502, "Finix returned a response that could not be processed.", inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            var reconciled = await TryReconcileAuthorizationAsync(orderId, claimKey, cancellationToken);
            if (reconciled is not null)
            {
                await CompleteClaimAsync(claim, reconciled.Id ?? string.Empty, cancellationToken);
                return ToPayResponse(payment);
            }

            await MarkClaimUnknownAsync(claim, "Authorization outcome unknown.", cancellationToken);
            throw new PaymentException(502,
                "The authorization outcome is unknown because the connection failed. Retry the payment; the same Finix idempotency key is reused.",
                inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            _logger.LogError(ex, "Finix credentials are not usable.");
            throw new PaymentException(502, "The payment provider is not configured correctly.", inner: ex);
        }

        if (authorization is null)
        {
            await MarkClaimUnknownAsync(claim, "Empty authorization response.", cancellationToken);
            throw new PaymentException(502, "Finix returned no authorization.");
        }

        ApplyAuthorization(payment, authorization);
        payment.Status = authorization.State == State1.Failed || authorization.State == State1.Canceled
            ? OrderPayment.PaymentFailed
            : OrderPayment.Authorized;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await CompleteClaimAsync(claim, authorization.Id ?? string.Empty, cancellationToken);

        _logger.LogInformation(
            "Authorized order {OrderId}: authorization {AuthorizationId} state {State} amount {AmountCents}",
            orderId, authorization.Id, authorization.State?.Value, payment.AmountCents);

        return ToPayResponse(payment);
    }

    // -------------------------------------------------------------- capture

    public async Task<FulfilOrderResponse> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var (_, payment) = await LoadOrderAsync(orderId, null, cancellationToken);

        if (payment.Status is OrderPayment.Captured or OrderPayment.PartiallyRefunded or OrderPayment.Refunded)
        {
            return ToFulfilResponse(payment, null);
        }

        if (payment.Status == OrderPayment.Canceled)
        {
            throw new PaymentException(409, "This order was canceled; its hold was released and cannot be captured.");
        }

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentException(409, "This order has not been paid; there is no authorization to capture.");
        }

        var claimKey = $"fulfil:{orderId}";
        var claimed = await TryClaimAsync(claimKey, "fulfil", null, orderId, cancellationToken);
        if (!claimed.IsNew)
        {
            if (claimed.Operation.State == PaymentOperation.Completed)
            {
                var refreshed = await LoadPaymentAsync(orderId, cancellationToken);
                if (refreshed.Status is OrderPayment.Captured or OrderPayment.PartiallyRefunded or OrderPayment.Refunded)
                {
                    return ToFulfilResponse(refreshed, null);
                }
            }

            if (claimed.Operation.State is PaymentOperation.Claimed or PaymentOperation.Unknown
                && await TrySettleCaptureAfterConnectionFailureAsync(payment, cancellationToken))
            {
                await CompleteClaimAsync(claimed.Operation, payment.CaptureTransferId, cancellationToken);
                return ToFulfilResponse(payment, null);
            }

            throw new PaymentException(409, "A fulfilment for this order is already in progress or its outcome is unknown.");
        }

        var claim = claimed.Operation;

        Authorization authorization;
        try
        {
            authorization = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = payment.AuthorizationId! },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<GetAuthorizationError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            await ReleaseClaimAsync(claim, cancellationToken);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (SdkException ex)
        {
            await MarkClaimUnknownAsync(claim, "Could not read the authorization.", cancellationToken);
            throw new PaymentException(502, "Could not read the authorization from Finix.", inner: ex);
        }

        var notCapturable = ExplainNotCapturable(authorization);
        if (notCapturable is not null)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            payment.FailureCode = authorization.FailureCode;
            payment.FailureMessage = notCapturable;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentException(409, notCapturable, authorization.FailureCode);
        }

        if (!string.IsNullOrWhiteSpace(authorization.Transfer))
        {
            var settled = await SettleCaptureAsync(payment, authorization, authorization.Transfer!, cancellationToken);
            await CompleteClaimAsync(claim, authorization.Transfer, cancellationToken);
            return ToFulfilResponse(payment, settled);
        }

        Authorization captured;
        try
        {
            captured = await _client.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = payment.AuthorizationId!,
                    Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                    {
                        CaptureAmount = payment.AmountCents
                    })
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            var explanation = await ExplainCaptureRefusalAsync(payment, message, cancellationToken);
            await ReleaseClaimAsync(claim, cancellationToken);
            throw new PaymentException(409, explanation, ex.StatusCode.ToString());
        }
        catch (ResponseDeserializationException ex)
        {
            await MarkClaimUnknownAsync(claim, "Unreadable capture response.", cancellationToken);
            throw new PaymentException(502, "Finix returned a response that could not be processed.", inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            var reconciled = await TrySettleCaptureAfterConnectionFailureAsync(payment, cancellationToken);
            if (reconciled)
            {
                await CompleteClaimAsync(claim, payment.CaptureTransferId, cancellationToken);
                return ToFulfilResponse(payment, null);
            }

            await MarkClaimUnknownAsync(claim, "Capture outcome unknown.", cancellationToken);
            throw new PaymentException(502,
                "The capture outcome is unknown because the connection failed. Re-check the order before retrying.",
                inner: ex);
        }

        var transferId = captured.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            transferId = await PollCaptureTransferAsync(payment.AuthorizationId!, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(transferId))
        {
            await MarkClaimUnknownAsync(claim, "Capture did not return a transfer id.", cancellationToken);
            throw new PaymentException(502,
                "Finix accepted the capture but has not reported the resulting transfer yet. Re-check the order.");
        }

        var note = await SettleCaptureAsync(payment, captured, transferId!, cancellationToken);
        await CompleteClaimAsync(claim, transferId, cancellationToken);

        _logger.LogInformation(
            "Captured order {OrderId}: transfer {TransferId} amount {AmountCents} fee {FeeCents} net {NetCents}",
            orderId, transferId, payment.CapturedAmountCents, payment.FeeCents, payment.NetCents);

        return ToFulfilResponse(payment, note);
    }

    // --------------------------------------------------------------- cancel

    public async Task<CancelOrderResponse> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var (_, payment) = await LoadOrderAsync(orderId, null, cancellationToken);

        if (payment.Status == OrderPayment.Canceled)
        {
            return ToCancelResponse(payment);
        }

        if (payment.Status is OrderPayment.Captured or OrderPayment.PartiallyRefunded or OrderPayment.Refunded)
        {
            throw new PaymentException(409, "This order is already captured; cancel is no longer possible. Use a refund instead.");
        }

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentException(409, "This order has not been paid; there is no hold to release.");
        }

        var claimKey = $"cancel:{orderId}";
        var claimed = await TryClaimAsync(claimKey, "cancel", null, orderId, cancellationToken);
        if (!claimed.IsNew)
        {
            if (claimed.Operation.State == PaymentOperation.Completed)
            {
                var refreshed = await LoadPaymentAsync(orderId, cancellationToken);
                if (refreshed.Status == OrderPayment.Canceled)
                {
                    return ToCancelResponse(refreshed);
                }
            }

            if (claimed.Operation.State is PaymentOperation.Claimed or PaymentOperation.Unknown
                && await TrySettleVoidAfterFailureAsync(payment, cancellationToken))
            {
                await CompleteClaimAsync(claimed.Operation, payment.AuthorizationId, cancellationToken);
                return ToCancelResponse(payment);
            }

            throw new PaymentException(409, "A cancellation for this order is already in progress or its outcome is unknown.");
        }

        var claim = claimed.Operation;

        Authorization voided;
        try
        {
            voided = await _client.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = payment.AuthorizationId!,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            var settled = await TrySettleVoidAfterFailureAsync(payment, cancellationToken);
            if (settled)
            {
                await CompleteClaimAsync(claim, payment.AuthorizationId, cancellationToken);
                return ToCancelResponse(payment);
            }

            await ReleaseClaimAsync(claim, cancellationToken);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (SdkConnectionException ex)
        {
            var settled = await TrySettleVoidAfterFailureAsync(payment, cancellationToken);
            if (settled)
            {
                await CompleteClaimAsync(claim, payment.AuthorizationId, cancellationToken);
                return ToCancelResponse(payment);
            }

            await MarkClaimUnknownAsync(claim, "Void outcome unknown.", cancellationToken);
            throw new PaymentException(502,
                "The release outcome is unknown because the connection failed. Re-check the order before retrying.",
                inner: ex);
        }

        ApplyVoid(payment, voided);
        payment.Status = OrderPayment.Canceled;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await CompleteClaimAsync(claim, payment.AuthorizationId, cancellationToken);

        _logger.LogInformation("Canceled order {OrderId}: authorization {AuthorizationId} voidState {VoidState}",
            orderId, payment.AuthorizationId, payment.VoidState);

        return ToCancelResponse(payment);
    }

    // --------------------------------------------------------------- refund

    public async Task<RefundOrderResponse> RefundOrderAsync(
        string buyerId,
        bool isAdmin,
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (_, payment) = await LoadOrderAsync(orderId, isAdmin ? null : buyerId, cancellationToken);

        if (payment.Status is not (OrderPayment.Captured or OrderPayment.PartiallyRefunded or OrderPayment.Refunded))
        {
            throw new PaymentException(409, "Only a captured payment can be refunded.");
        }

        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw new PaymentException(409, "The captured transfer id is missing; the refund cannot be addressed.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException(400, "An idempotencyKey is required for refunds.");
        }

        var claimKey = $"refund:{orderId}:{request.IdempotencyKey}";
        var claimed = await TryClaimAsync(claimKey, "refund", buyerId, orderId, cancellationToken);
        if (!claimed.IsNew)
        {
            if (claimed.Operation.State == PaymentOperation.Completed && !string.IsNullOrWhiteSpace(claimed.Operation.ResultId))
            {
                var existing = await _db.RefundRecords
                    .FirstOrDefaultAsync(r => r.OrderId == orderId && r.RefundId == claimed.Operation.ResultId, cancellationToken);
                if (existing is not null)
                {
                    return ToRefundResponse(payment, existing);
                }
            }

            if (claimed.Operation.State is PaymentOperation.Claimed or PaymentOperation.Unknown)
            {
                var reconciled = await TryReconcileRefundAsync(
                    payment,
                    $"refund:{payment.ProviderKey}:{request.IdempotencyKey}",
                    request.AmountCents ?? 0,
                    cancellationToken);
                if (reconciled is not null)
                {
                    await CompleteClaimAsync(claimed.Operation, reconciled.RefundId, cancellationToken);
                    return ToRefundResponse(payment, reconciled);
                }
            }

            throw new PaymentException(409, "A refund with this idempotency key is already in progress or its outcome is unknown.");
        }

        var claim = claimed.Operation;

        var captured = payment.CapturedAmountCents ?? 0;
        await WaitForTransferSettledAsync(payment.CaptureTransferId!, cancellationToken);
        var alreadyRefunded = await GetRefundedCentsAsync(payment, cancellationToken);
        var remaining = captured - alreadyRefunded;
        if (remaining <= 0)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            throw new PaymentException(409, "The captured payment has already been fully refunded.");
        }

        var amount = request.AmountCents ?? remaining;
        if (amount <= 0)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            throw new PaymentException(400, "The refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            await ReleaseClaimAsync(claim, cancellationToken);
            throw new PaymentException(409,
                $"The refund amount {amount} exceeds the remaining refundable amount {remaining}.");
        }

        var refundBody = new RefundTransfer { RefundAmount = checked((int)amount) };
        refundBody.AdditionalProperties.Set("idempotency_id", $"refund:{payment.ProviderKey}:{request.IdempotencyKey}");

        Transfer? reversal;
        try
        {
            reversal = await _client.TransfersApi.CreateTransferReversal(
                new CreateTransferReversalRequest
                {
                    TransferId = payment.CaptureTransferId!,
                    Body = TransfersReversalsRequest.RefundTransfer(refundBody)
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            await ReleaseClaimAsync(claim, cancellationToken);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            await MarkClaimUnknownAsync(claim, "Unreadable refund response.", cancellationToken);
            throw new PaymentException(502, "Finix returned a response that could not be processed.", inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            var reconciled = await TryReconcileRefundAsync(payment, $"refund:{payment.ProviderKey}:{request.IdempotencyKey}", amount, cancellationToken);
            if (reconciled is not null)
            {
                await CompleteClaimAsync(claim, reconciled.RefundId, cancellationToken);
                return ToRefundResponse(payment, reconciled);
            }

            await MarkClaimUnknownAsync(claim, "Refund outcome unknown.", cancellationToken);
            throw new PaymentException(502,
                "The refund outcome is unknown because the connection failed. Retry with the same idempotencyKey.",
                inner: ex);
        }

        if (reversal is null || string.IsNullOrWhiteSpace(reversal.Id))
        {
            await MarkClaimUnknownAsync(claim, "Empty refund response.", cancellationToken);
            throw new PaymentException(502, "Finix returned no refund id.");
        }

        var record = new RefundRecord
        {
            OrderId = orderId,
            RefundId = reversal.Id!,
            AmountCents = reversal.Amount ?? amount,
            IdempotencyKey = request.IdempotencyKey,
            State = reversal.State?.Value ?? "UNKNOWN",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.RefundRecords.Add(record);

        payment.RefundedCents = alreadyRefunded + record.AmountCents;
        payment.Status = payment.RefundedCents >= captured
            ? OrderPayment.Refunded
            : OrderPayment.PartiallyRefunded;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        await CompleteClaimAsync(claim, record.RefundId, cancellationToken);

        _logger.LogInformation("Refunded order {OrderId}: refund {RefundId} amount {AmountCents} total refunded {RefundedCents}",
            orderId, record.RefundId, record.AmountCents, payment.RefundedCents);

        return ToRefundResponse(payment, record);
    }

    // -------------------------------------------------------- saved cards

    public async Task<CreatePaymentMethodResponse> CreatePaymentMethodAsync(
        string buyerId,
        CreatePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Card is null)
        {
            throw new PaymentException(400, "Card details are required.");
        }

        ValidateCard(request.Card);

        PaymentOperation? claim = null;
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var claimKey = $"card:{buyerId}:{request.IdempotencyKey}";
            var claimed = await TryClaimAsync(claimKey, "card", buyerId, null, cancellationToken);
            if (!claimed.IsNew)
            {
                if (claimed.Operation.State == PaymentOperation.Completed && !string.IsNullOrWhiteSpace(claimed.Operation.ResultId))
                {
                    var existing = await _db.SavedCards
                        .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.PaymentInstrumentId == claimed.Operation.ResultId, cancellationToken);
                    if (existing is not null)
                    {
                        return ToCreatePaymentMethodResponse(existing);
                    }
                }

                throw new PaymentException(409, "A request with this idempotency key is already in progress.");
            }

            claim = claimed.Operation;
        }

        var identityId = await GetOrCreateIdentityIdAsync(buyerId, cancellationToken);
        PaymentInstrumentCard instrument;
        try
        {
            instrument = await CreateCardInstrumentAsync(identityId, request.Card, "saved", buyerId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode >= 500)
        {
            if (claim is not null)
            {
                await MarkClaimUnknownAsync(claim, "Saved-card outcome unknown.", cancellationToken);
            }

            throw;
        }
        catch
        {
            if (claim is not null)
            {
                await ReleaseClaimAsync(claim, cancellationToken);
            }

            throw;
        }

        var saved = new SavedCard
        {
            BuyerId = buyerId,
            FinixIdentityId = identityId,
            PaymentInstrumentId = instrument.Id!,
            Last4 = instrument.LastFour ?? string.Empty,
            Brand = instrument.Brand?.Value,
            ExpirationMonth = instrument.ExpirationMonth ?? request.Card.ExpirationMonth,
            ExpirationYear = instrument.ExpirationYear ?? request.Card.ExpirationYear,
            Enabled = instrument.Enabled != false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.SavedCards.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);

        if (claim is not null)
        {
            await CompleteClaimAsync(claim, saved.PaymentInstrumentId, cancellationToken);
        }

        _logger.LogInformation("Saved card {PaymentMethodId} for {BuyerId} ending {Last4}",
            saved.PaymentInstrumentId, buyerId, saved.Last4);

        return ToCreatePaymentMethodResponse(saved);
    }

    public async Task<ListPaymentMethodsResponse> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken)
    {
        var cards = await _db.SavedCards
            .Where(c => c.BuyerId == buyerId && c.Enabled)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var response = new ListPaymentMethodsResponse();
        foreach (var card in cards)
        {
            response.PaymentMethods.Add(new PaymentMethodDto
            {
                PaymentMethodId = card.PaymentInstrumentId,
                Last4 = card.Last4,
                Brand = card.Brand,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear
            });
        }

        return response;
    }

    public async Task DeletePaymentMethodAsync(string buyerId, string paymentMethodId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId))
        {
            throw new PaymentException(400, "paymentMethodId is required.");
        }

        var card = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.PaymentInstrumentId == paymentMethodId, cancellationToken);
        if (card is null)
        {
            throw new PaymentException(404, "Saved card not found.");
        }

        if (!card.Enabled)
        {
            return;
        }

        var claimKey = $"disable:{buyerId}:{paymentMethodId}";
        var claimed = await TryClaimAsync(claimKey, "disable", buyerId, null, cancellationToken);
        if (!claimed.IsNew)
        {
            if (claimed.Operation.State == PaymentOperation.Completed)
            {
                card.Enabled = false;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (claimed.Operation.State is PaymentOperation.Claimed or PaymentOperation.Unknown
                && await IsInstrumentDisabledAsync(paymentMethodId, cancellationToken))
            {
                card.Enabled = false;
                await _db.SaveChangesAsync(cancellationToken);
                await CompleteClaimAsync(claimed.Operation, paymentMethodId, cancellationToken);
                return;
            }

            throw new PaymentException(409, "Disabling this saved card is already in progress or its outcome is unknown.");
        }

        var claim = claimed.Operation;

        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = paymentMethodId,
                    Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                        new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at Finix; reflect that locally.
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            await ReleaseClaimAsync(claim, cancellationToken);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (SdkConnectionException ex)
        {
            var settled = await IsInstrumentDisabledAsync(paymentMethodId, cancellationToken);
            if (!settled)
            {
                await MarkClaimUnknownAsync(claim, "Disable outcome unknown.", cancellationToken);
                throw new PaymentException(502, "Could not confirm the saved card was disabled.", inner: ex);
            }
        }

        card.Enabled = false;
        await _db.SaveChangesAsync(cancellationToken);
        await CompleteClaimAsync(claim, paymentMethodId, cancellationToken);

        _logger.LogInformation("Disabled saved card {PaymentMethodId} for {BuyerId}", paymentMethodId, buyerId);
    }

    // -------------------------------------------------------- reconciliation

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentException(400, "from must be earlier than or equal to to.");
        }

        var complete = true;
        if (from < DateTimeOffset.UtcNow.AddDays(-30))
        {
            // Finix only exposes the last 30 days when no explicit created_at filter is sent, and
            // this SDK's date filter is not accepted by the sandbox API.
            complete = false;
        }

        List<Authorization> authorizations;
        List<Transfer> transfers;
        try
        {
            authorizations = await DrainAuthorizationsAsync(from, to, () => complete = false, cancellationToken);
            transfers = await DrainTransfersAsync(from, to, () => complete = false, cancellationToken);
        }
        catch (ApiException<ListAuthorizationsError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (ApiException<ListTransfersError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (SdkException ex)
        {
            throw new PaymentException(502, "Could not read Finix transactions for the requested range.", inner: ex);
        }

        authorizations = authorizations
            .Where(a => a.CreatedAt is null || (a.CreatedAt >= from && a.CreatedAt <= to))
            .ToList();
        transfers = transfers
            .Where(t => t.CreatedAt is null || (t.CreatedAt >= from && t.CreatedAt <= to))
            .ToList();

        var payments = await _db.OrderPayments.ToListAsync(cancellationToken);
        var refunds = await _db.RefundRecords.ToListAsync(cancellationToken);

        var paymentsByAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.AuthorizationId))
            .GroupBy(p => p.AuthorizationId!)
            .ToDictionary(g => g.Key, g => g.First());
        var paymentsByCapture = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.CaptureTransferId))
            .GroupBy(p => p.CaptureTransferId!)
            .ToDictionary(g => g.Key, g => g.First());
        var refundsByTransfer = refunds
            .GroupBy(r => r.RefundId)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconciliationResponse { From = from, To = to, Complete = complete };
        var matchedOrderIds = new HashSet<int>();

        foreach (var authorization in authorizations)
        {
            var entry = new ReconciliationEntryDto
            {
                Kind = "authorization",
                FinixId = authorization.Id ?? string.Empty,
                AmountCents = authorization.Amount,
                State = authorization.State?.Value,
                CreatedAt = authorization.CreatedAt
            };

            if (authorization.Id is not null && paymentsByAuthorization.TryGetValue(authorization.Id, out var payment))
            {
                entry.OrderId = payment.OrderId;
                entry.OrderStatus = payment.Status;
                entry.MatchStatus = "matched";
                matchedOrderIds.Add(payment.OrderId);
                response.MatchedCount++;
            }
            else
            {
                entry.MatchStatus = "finixOnly";
                response.FinixOnlyCount++;
            }

            response.Entries.Add(entry);
        }

        foreach (var transfer in transfers)
        {
            var entry = new ReconciliationEntryDto
            {
                Kind = transfer.Type?.Value?.ToLowerInvariant() ?? "transfer",
                FinixId = transfer.Id ?? string.Empty,
                AmountCents = transfer.Amount,
                State = transfer.State?.Value,
                CreatedAt = transfer.CreatedAt
            };

            OrderPayment? payment = null;
            RefundRecord? refund = null;
            if (transfer.Id is not null)
            {
                paymentsByCapture.TryGetValue(transfer.Id, out payment);
                refundsByTransfer.TryGetValue(transfer.Id, out refund);
            }

            if (payment is not null)
            {
                entry.OrderId = payment.OrderId;
                entry.OrderStatus = payment.Status;
                entry.MatchStatus = "matched";
                matchedOrderIds.Add(payment.OrderId);
                response.MatchedCount++;
            }
            else if (refund is not null)
            {
                entry.OrderId = refund.OrderId;
                entry.MatchStatus = "matched";
                matchedOrderIds.Add(refund.OrderId);
                response.MatchedCount++;
            }
            else
            {
                entry.MatchStatus = "finixOnly";
                response.FinixOnlyCount++;
            }

            response.Entries.Add(entry);
        }

        foreach (var payment in payments.Where(p => p.CreatedAt >= from && p.CreatedAt <= to && !matchedOrderIds.Contains(p.OrderId)))
        {
            response.Entries.Add(new ReconciliationEntryDto
            {
                Kind = "order",
                FinixId = payment.AuthorizationId ?? string.Empty,
                OrderId = payment.OrderId,
                AmountCents = payment.AmountCents,
                State = payment.Status,
                CreatedAt = payment.CreatedAt,
                MatchStatus = "eshopOnly",
                OrderStatus = payment.Status
            });
            response.EshopOnlyCount++;
        }

        return response;
    }

    // ------------------------------------------------------------- helpers

    private string NormalizedCurrency =>
        string.Equals(_finix.Currency, "CAD", StringComparison.OrdinalIgnoreCase) ? "CAD" : "USD";

    private FinixCurrency MapCurrency() =>
        string.Equals(_finix.Currency, "CAD", StringComparison.OrdinalIgnoreCase) ? FinixCurrency.Cad : FinixCurrency.Usd;

    private async Task<(Order Order, OrderPayment Payment)> LoadOrderAsync(
        int orderId,
        string? buyerId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new PaymentException(404, "Order not found.");
        }

        if (buyerId is not null && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that another shopper's order exists.
            throw new PaymentException(404, "Order not found.");
        }

        var payment = await _db.OrderPayments
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

        if (payment is null)
        {
            throw new PaymentException(409, "This order has no payment record.");
        }

        return (order, payment);
    }

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _db.OrderPayments
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentException(404, "Order payment not found.");
        }

        return payment;
    }

    private async Task<string> ResolveSavedCardSourceAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        var card = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.BuyerId == buyerId && c.PaymentInstrumentId == paymentMethodId, cancellationToken);

        if (card is null || !card.Enabled)
        {
            throw new PaymentException(404, "Saved card not found.");
        }

        try
        {
            var instrument = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentMethodId },
                cancellationToken: cancellationToken);

            if (!instrument.TryGetPaymentInstrumentCard(out var cardInstrument)
                || cardInstrument is null
                || cardInstrument.Enabled == false
                || !string.Equals(cardInstrument.Identity, card.FinixIdentityId, StringComparison.Ordinal))
            {
                throw new PaymentException(409, "This saved card is no longer usable.");
            }
        }
        catch (ApiException<GetPaymentInstrumentError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PaymentException(404, "Saved card not found.", inner: ex);
        }
        catch (ApiException<GetPaymentInstrumentError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new PaymentException(502, "Could not verify the saved card with Finix.", inner: ex);
        }

        return card.PaymentInstrumentId;
    }

    private async Task<string> GetOrCreateIdentityIdAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _db.ShopperIdentities.FindAsync(new object?[] { buyerId }, cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var entity = new Entity5
        {
            Email = buyerId.Contains('@', StringComparison.Ordinal) ? buyerId : null
        };
        var buyer = new Buyer
        {
            Entity = entity,
            Tags = new Dictionary<string, string> { ["buyerId"] = buyerId }
        };

        FinixIdentity identity;
        try
        {
            identity = await _client.Identities.CreateIdentity(
                new CreateIdentityRequest { Body = IdentitiesRequest.Buyer(buyer) },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<CreateIdentityError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new PaymentException(502, "Could not create a buyer identity at Finix.", inner: ex);
        }

        if (string.IsNullOrWhiteSpace(identity?.Id))
        {
            throw new PaymentException(502, "Finix returned no buyer identity id.");
        }

        _db.ShopperIdentities.Add(new ShopperIdentity
        {
            BuyerId = buyerId,
            FinixIdentityId = identity.Id!,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request for the same shopper won the race; use the stored identity.
            _db.ChangeTracker.Clear();
            var raced = await _db.ShopperIdentities.FindAsync(new object?[] { buyerId }, cancellationToken);
            if (raced is not null)
            {
                return raced.FinixIdentityId;
            }

            throw;
        }

        return identity.Id!;
    }

    private async Task<PaymentInstrumentCard> CreateCardInstrumentAsync(
        string identityId,
        CardDetails card,
        string purpose,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var model = new PaymentInstrumentCard1
        {
            Number = NormalizeCardNumber(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name,
            SecurityCode = card.SecurityCode,
            Address = MapCardAddress(card.Address),
            Tags = new Dictionary<string, string>
            {
                ["buyerId"] = buyerId,
                ["purpose"] = purpose
            }
        };

        try
        {
            var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest
                {
                    Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(model)
                },
                cancellationToken: cancellationToken);

            if (!response.TryGetPaymentInstrumentCard(out var instrument)
                || instrument is null
                || string.IsNullOrWhiteSpace(instrument.Id))
            {
                throw new PaymentException(502, "Finix returned no card payment instrument.");
            }

            return instrument;
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            var message = Describe(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null);
            throw TranslateApiError(ex.StatusCode, message, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw new PaymentException(502, "Finix returned a response that could not be processed.", inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new PaymentException(502, "The card could not be tokenized at Finix.", inner: ex);
        }
    }

    private static void ApplyAuthorization(OrderPayment payment, Authorization authorization)
    {
        payment.AuthorizationId = authorization.Id;
        payment.AuthorizationState = authorization.State?.Value;
        payment.IsVoid = authorization.IsVoid == true;
        payment.VoidState = authorization.VoidState?.Value;
        payment.AuthorizationExpiresAt = authorization.ExpiresAt;
        payment.FailureCode = authorization.FailureCode;
        payment.FailureMessage = authorization.FailureMessage;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyVoid(OrderPayment payment, Authorization authorization)
    {
        payment.AuthorizationState = authorization.State?.Value;
        payment.IsVoid = authorization.IsVoid == true
            || authorization.VoidState == VoidState.Succeeded;
        payment.VoidState = authorization.VoidState?.Value;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? ExplainNotCapturable(Authorization authorization)
    {
        if (authorization.IsVoid == true || authorization.VoidState == VoidState.Succeeded)
        {
            return "The authorization was voided, so its funds were released and it can no longer be captured. Place a new order and authorize it again.";
        }

        if (authorization.State == State1.Failed || authorization.State == State1.Canceled)
        {
            return BuildFailureExplanation("The authorization is no longer active", authorization);
        }

        if (authorization.ExpiresAt is { } expiresAt && expiresAt < DateTimeOffset.UtcNow)
        {
            return $"The authorization hold expired at {expiresAt:O}, so it can no longer be captured. Place a new order and authorize it again.";
        }

        if (!string.IsNullOrWhiteSpace(authorization.FailureCode))
        {
            return BuildFailureExplanation("The authorization failed", authorization);
        }

        return null;
    }

    private static string BuildFailureExplanation(string prefix, Authorization authorization)
    {
        var code = string.IsNullOrWhiteSpace(authorization.FailureCode) ? "unknown" : authorization.FailureCode;
        var message = string.IsNullOrWhiteSpace(authorization.FailureMessage)
            ? "Finix did not provide a failure message."
            : authorization.FailureMessage;
        return $"{prefix} (code {code}): {message}";
    }

    private async Task<string> ExplainCaptureRefusalAsync(
        OrderPayment payment,
        string providerMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = payment.AuthorizationId! },
                cancellationToken: cancellationToken);

            var reason = ExplainNotCapturable(latest);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }

            if (!string.IsNullOrWhiteSpace(latest.Transfer))
            {
                return "Finix reports the authorization is already captured.";
            }
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not re-read authorization {AuthorizationId} after a refused capture",
                payment.AuthorizationId);
        }

        return $"Finix refused the capture: {providerMessage}";
    }

    private async Task<string?> SettleCaptureAsync(
        OrderPayment payment,
        Authorization captured,
        string transferId,
        CancellationToken cancellationToken)
    {
        Transfer? transfer = null;
        try
        {
            transfer = await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: cancellationToken);

            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (transfer is not null
                    && (transfer.State == State11.Succeeded || transfer.State == State11.Failed)
                    && transfer.Fee is not null)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                transfer = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: cancellationToken);
            }
        }
        catch (ApiException<GetTransferError> ex)
        {
            _logger.LogWarning(ex, "Captured order {OrderId} but could not read transfer {TransferId}",
                payment.OrderId, transferId);
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Captured order {OrderId} but could not read transfer {TransferId}",
                payment.OrderId, transferId);
        }

        payment.AuthorizationState = captured.State?.Value ?? payment.AuthorizationState;
        payment.CaptureTransferId = transferId;
        payment.CapturedAmountCents = transfer?.Amount ?? payment.AmountCents;
        payment.FeeCents = transfer?.Fee;
        payment.NetCents = transfer?.Fee is { } fee
            ? (payment.CapturedAmountCents ?? 0) - fee
            : payment.CapturedAmountCents;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        if (transfer?.State is { } state
            && (state == State11.Failed || state == State11.Canceled || state == State11.Returned))
        {
            payment.Status = OrderPayment.PaymentFailed;
            payment.FailureCode = transfer.FailureCode;
            payment.FailureMessage = string.IsNullOrWhiteSpace(transfer.FailureMessage)
                ? $"The capture transfer ended in state {state.Value}."
                : transfer.FailureMessage;
            await _db.SaveChangesAsync(cancellationToken);
            return payment.FailureMessage;
        }

        payment.Status = OrderPayment.Captured;
        await _db.SaveChangesAsync(cancellationToken);

        if (transfer?.State == State11.Pending)
        {
            return "Capture was accepted but Finix still reports the transfer as PENDING; refunds may need to wait for settlement.";
        }

        return transfer?.Fee is null
            ? "Capture succeeded. Finix has not reported the fee yet; fee and net are provisional."
            : null;
    }

    private async Task WaitForTransferSettledAsync(string transferId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                var transfer = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: cancellationToken);

                if (transfer.State == State11.Succeeded
                    || transfer.State == State11.Failed
                    || transfer.State == State11.Canceled
                    || transfer.State == State11.Returned)
                {
                    return;
                }
            }
            catch (SdkException ex)
            {
                _logger.LogWarning(ex, "Could not read transfer {TransferId} while waiting for settlement", transferId);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private async Task<bool> TrySettleCaptureAfterConnectionFailureAsync(
        OrderPayment payment,
        CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = payment.AuthorizationId! },
                cancellationToken: cancellationToken);

            if (!string.IsNullOrWhiteSpace(latest.Transfer))
            {
                await SettleCaptureAsync(payment, latest, latest.Transfer!, cancellationToken);
                return true;
            }
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not settle capture outcome for order {OrderId}", payment.OrderId);
        }

        return false;
    }

    private async Task<string?> PollCaptureTransferAsync(string authorizationId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            try
            {
                var latest = await _client.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = authorizationId },
                    cancellationToken: cancellationToken);

                if (!string.IsNullOrWhiteSpace(latest.Transfer))
                {
                    return latest.Transfer;
                }
            }
            catch (SdkException ex)
            {
                _logger.LogWarning(ex, "Could not poll authorization {AuthorizationId} for a capture transfer", authorizationId);
            }
        }

        return null;
    }

    private async Task<bool> TrySettleVoidAfterFailureAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = payment.AuthorizationId! },
                cancellationToken: cancellationToken);

            if (latest.IsVoid == true || latest.VoidState == VoidState.Succeeded)
            {
                ApplyVoid(payment, latest);
                payment.Status = OrderPayment.Canceled;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not settle void outcome for order {OrderId}", payment.OrderId);
        }

        return false;
    }

    private async Task<Authorization?> TryReconcileAuthorizationAsync(
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _client.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    IdempotencyId = idempotencyKey,
                    Limit = PageSize
                },
                cancellationToken: cancellationToken);

            var match = page.Embedded?.Authorizations?
                .FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyKey, StringComparison.Ordinal));

            if (match is not null)
            {
                var payment = await LoadPaymentAsync(orderId, cancellationToken);
                ApplyAuthorization(payment, match);
                payment.Status = match.State == State1.Failed || match.State == State1.Canceled
                    ? OrderPayment.PaymentFailed
                    : OrderPayment.Authorized;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return match;
            }
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not reconcile authorization outcome for order {OrderId}", orderId);
        }

        return null;
    }

    private async Task<RefundRecord?> TryReconcileRefundAsync(
        OrderPayment payment,
        string idempotencyKey,
        long requestedAmount,
        CancellationToken cancellationToken)
    {
        try
        {
            var (reversals, complete) = await DrainReversalsAsync(payment.CaptureTransferId!, cancellationToken);
            if (!complete)
            {
                return null;
            }

            var match = reversals.FirstOrDefault(r =>
                string.Equals(r.IdempotencyId, idempotencyKey, StringComparison.Ordinal)
                || (r.Amount == requestedAmount && r.CreatedAt >= payment.UpdatedAt.AddMinutes(-5)));

            if (match?.Id is null)
            {
                return null;
            }

            var alreadyRecorded = await _db.RefundRecords
                .FirstOrDefaultAsync(r => r.OrderId == payment.OrderId && r.RefundId == match.Id, cancellationToken);
            if (alreadyRecorded is not null)
            {
                return alreadyRecorded;
            }

            var record = new RefundRecord
            {
                OrderId = payment.OrderId,
                RefundId = match.Id,
                AmountCents = match.Amount ?? requestedAmount,
                IdempotencyKey = idempotencyKey,
                State = match.State?.Value ?? "UNKNOWN",
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.RefundRecords.Add(record);
            payment.RefundedCents += record.AmountCents;
            payment.Status = payment.CapturedAmountCents is { } captured && payment.RefundedCents >= captured
                ? OrderPayment.Refunded
                : OrderPayment.PartiallyRefunded;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return record;
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not reconcile refund outcome for order {OrderId}", payment.OrderId);
            return null;
        }
    }

    private async Task<long> GetRefundedCentsAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        var stored = await _db.RefundRecords
            .Where(r => r.OrderId == payment.OrderId)
            .SumAsync(r => (long?)r.AmountCents, cancellationToken) ?? 0;

        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            return Math.Max(stored, payment.RefundedCents);
        }

        try
        {
            var (reversals, complete) = await DrainReversalsAsync(payment.CaptureTransferId!, cancellationToken);
            if (!complete)
            {
                throw new PaymentException(409,
                    "Could not read the complete refund history from Finix; refusing to compute a refund ceiling from partial data.");
            }

            var providerTotal = reversals
                .Where(r => r.State != State11.Failed && r.State != State11.Canceled)
                .Sum(r => (long)(r.Amount ?? 0));
            return Math.Max(stored, Math.Max(providerTotal, payment.RefundedCents));
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not list reversals for transfer {TransferId}; using stored refund total",
                payment.CaptureTransferId);
            return Math.Max(stored, payment.RefundedCents);
        }
    }

    private async Task<List<Authorization>> DrainAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        Action onTruncated,
        CancellationToken cancellationToken)
    {
        var results = new List<Authorization>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _client.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    Merchant = _finix.MerchantId,
                    Limit = PageSize,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            if (response.Embedded?.Authorizations is { } items)
            {
                results.AddRange(items);
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, cursor, StringComparison.Ordinal))
            {
                return results;
            }

            cursor = next;
        }

        onTruncated();
        return results;
    }

    private async Task<List<Transfer>> DrainTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        Action onTruncated,
        CancellationToken cancellationToken)
    {
        var results = new List<Transfer>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _client.TransfersApi.ListTransfers(
                new ListTransfersRequest
                {
                    Merchant = _finix.MerchantId,
                    Limit = PageSize,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            if (response.Embedded?.Transfers is { } items)
            {
                results.AddRange(items);
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, cursor, StringComparison.Ordinal))
            {
                return results;
            }

            cursor = next;
        }

        onTruncated();
        return results;
    }

    private async Task<(List<Transfer> Items, bool Complete)> DrainReversalsAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var results = new List<Transfer>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _client.TransfersApi.ListTransferReversals(
                new ListTransferReversalsRequest
                {
                    TransferId = transferId,
                    Limit = PageSize,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            if (response.Embedded?.Transfers is { } items)
            {
                results.AddRange(items);
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, cursor, StringComparison.Ordinal))
            {
                return (results, true);
            }

            cursor = next;
        }

        return (results, false);
    }

    private async Task<bool> IsInstrumentDisabledAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            var instrument = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                cancellationToken: cancellationToken);

            return instrument.TryGetPaymentInstrumentCard(out var card) && card?.Enabled == false;
        }
        catch (ApiException<GetPaymentInstrumentError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return true;
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Could not verify disabled state of payment instrument {PaymentInstrumentId}",
                paymentInstrumentId);
            return false;
        }
    }

    private async Task<(bool IsNew, PaymentOperation Operation)> TryClaimAsync(
        string key,
        string kind,
        string? buyerId,
        int? orderId,
        CancellationToken cancellationToken)
    {
        var existing = await _db.PaymentOperations.FindAsync(new object?[] { key }, cancellationToken);
        if (existing is not null)
        {
            if ((existing.State == PaymentOperation.Claimed || existing.State == PaymentOperation.Unknown)
                && existing.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-5))
            {
                _db.PaymentOperations.Remove(existing);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                return (false, existing);
            }
        }

        var claim = new PaymentOperation
        {
            Key = key,
            Kind = kind,
            BuyerId = buyerId,
            OrderId = orderId,
            State = PaymentOperation.Claimed,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.PaymentOperations.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return (true, claim);
        }
        catch (DbUpdateException)
        {
            _db.Entry(claim).State = EntityState.Detached;
        }
        catch (InvalidOperationException)
        {
            _db.Entry(claim).State = EntityState.Detached;
        }

        var raced = await _db.PaymentOperations.FindAsync(new object?[] { key }, cancellationToken);
        if (raced is null)
        {
            throw new PaymentException(409, "Could not establish the payment claim; retry the request.");
        }

        return (false, raced);
    }

    private async Task CompleteClaimAsync(PaymentOperation claim, string? resultId, CancellationToken cancellationToken)
    {
        claim.State = PaymentOperation.Completed;
        claim.ResultId = resultId;
        claim.CompletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkClaimUnknownAsync(PaymentOperation claim, string reason, CancellationToken cancellationToken)
    {
        claim.State = PaymentOperation.Unknown;
        claim.Error = reason;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseClaimAsync(PaymentOperation claim, CancellationToken cancellationToken)
    {
        _db.PaymentOperations.Remove(claim);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordPaymentFailureAsync(
        OrderPayment payment,
        string message,
        CancellationToken cancellationToken)
    {
        payment.Status = OrderPayment.PaymentFailed;
        payment.FailureMessage = message;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static PaymentException TranslateApiError(HttpStatusCode status, string message, Exception inner)
    {
        var code = (int)status;
        return code switch
        {
            401 or 403 => new PaymentException(502, "The payment provider rejected this application's credentials.", code.ToString(), inner),
            429 => new PaymentException(503, "The payment provider is rate limiting requests; retry shortly.", code.ToString(), inner),
            >= 400 and < 500 => new PaymentException(code, message, code.ToString(), inner),
            _ => new PaymentException(502, message, code.ToString(), inner)
        };
    }

    private static string Describe(GenericError? error)
    {
        var messages = error?.Embedded?.Errors?
            .Select(e => string.IsNullOrWhiteSpace(e.Code) ? e.Message : $"{e.Code}: {e.Message}")
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();

        return messages is { Count: > 0 }
            ? string.Join("; ", messages)
            : "Finix rejected the request.";
    }

    private static string Describe<TError>(ApiException<TError> exception, GenericError? generic)
        where TError : ApiError
    {
        if (generic is not null)
        {
            return Describe(generic);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            var body = raw.ReadAsString();
            if (!string.IsNullOrWhiteSpace(body))
            {
                return body;
            }
        }

        return $"Finix rejected the request (HTTP {(int)exception.StatusCode}).";
    }

    private static OrderAddress MapShippingAddress(ShippingAddressRequest? address)
    {
        if (address is null)
        {
            return new OrderAddress("N/A", "N/A", "N/A", "USA", "00000");
        }

        return new OrderAddress(
            string.IsNullOrWhiteSpace(address.Street) ? "N/A" : address.Street!,
            string.IsNullOrWhiteSpace(address.City) ? "N/A" : address.City!,
            string.IsNullOrWhiteSpace(address.State) ? "N/A" : address.State!,
            string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country!,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "00000" : address.ZipCode!);
    }

    private static FinixAddress? MapCardAddress(CardAddressDetails? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Line1))
        {
            return null;
        }

        var country = address.Country?.Trim().ToUpperInvariant() switch
        {
            "CAN" or "CA" or "CANADA" => Country.Can,
            _ => Country.Usa
        };

        return new FinixAddress
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = string.IsNullOrWhiteSpace(address.City) ? "N/A" : address.City,
            Region = string.IsNullOrWhiteSpace(address.Region) ? "NA" : address.Region,
            PostalCode = string.IsNullOrWhiteSpace(address.PostalCode) ? "00000" : address.PostalCode,
            Country = country
        };
    }

    private static void ValidateCard(CardDetails card)
    {
        var digits = NormalizeCardNumber(card.Number);
        if (digits.Length is < 12 or > 19 || digits.Any(c => c < '0' || c > '9'))
        {
            throw new PaymentException(400, "Card number is not valid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentException(400, "Card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentException(400, "Card expiration year is in the past.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentException(400, "Cardholder name is required.");
        }
    }

    private static string NormalizeCardNumber(string number)
    {
        var builder = new StringBuilder(number?.Length ?? 0);
        foreach (var ch in number ?? string.Empty)
        {
            if (ch is ' ' or '-')
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static long ToCents(decimal amount)
    {
        var cents = amount * 100m;
        if (cents != decimal.Truncate(cents))
        {
            throw new PaymentException(400, "The order total cannot be represented in whole cents.");
        }

        if (cents <= 0)
        {
            throw new PaymentException(400, "The order total must be greater than zero.");
        }

        return (long)cents;
    }

    private CreateOrderResponse ToCreateOrderResponse(int orderId, OrderPayment payment) => new()
    {
        OrderId = orderId,
        AmountCents = payment.AmountCents,
        Currency = payment.Currency,
        Status = payment.Status
    };

    private static PayOrderResponse ToPayResponse(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState,
        AmountCents = payment.AmountCents,
        Currency = payment.Currency
    };

    private static FulfilOrderResponse ToFulfilResponse(OrderPayment payment, string? message) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status,
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState,
        CaptureTransferId = payment.CaptureTransferId,
        CapturedAmountCents = payment.CapturedAmountCents,
        FeeCents = payment.FeeCents,
        NetCents = payment.NetCents,
        Currency = payment.Currency,
        Message = message
    };

    private static CancelOrderResponse ToCancelResponse(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status,
        AuthorizationId = payment.AuthorizationId,
        IsVoid = payment.IsVoid,
        VoidState = payment.VoidState
    };

    private static RefundOrderResponse ToRefundResponse(OrderPayment payment, RefundRecord refund) => new()
    {
        OrderId = payment.OrderId,
        RefundId = refund.RefundId,
        AmountCents = refund.AmountCents,
        RefundedCents = payment.RefundedCents,
        RemainingRefundableCents = Math.Max(0, (payment.CapturedAmountCents ?? 0) - payment.RefundedCents),
        Status = payment.Status
    };

    private static CreatePaymentMethodResponse ToCreatePaymentMethodResponse(SavedCard card) => new()
    {
        PaymentMethodId = card.PaymentInstrumentId,
        Last4 = card.Last4,
        Brand = card.Brand,
        ExpirationMonth = card.ExpirationMonth,
        ExpirationYear = card.ExpirationYear
    };

    private static OrderSummaryDto ToOrderSummary(Order order, OrderPayment? payment)
    {
        var summary = new OrderSummaryDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            AmountCents = payment?.AmountCents ?? 0,
            Currency = payment?.Currency ?? string.Empty,
            Status = payment?.Status ?? "UNKNOWN",
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationState = payment?.AuthorizationState,
            IsVoid = payment?.IsVoid ?? false,
            VoidState = payment?.VoidState,
            CaptureTransferId = payment?.CaptureTransferId,
            CapturedAmountCents = payment?.CapturedAmountCents,
            FeeCents = payment?.FeeCents,
            NetCents = payment?.NetCents,
            RefundedCents = payment?.RefundedCents ?? 0,
            FailureCode = payment?.FailureCode,
            FailureMessage = payment?.FailureMessage
        };

        foreach (var item in order.OrderItems)
        {
            summary.Items.Add(new OrderItemSummaryDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units,
                LineTotal = item.UnitPrice * item.Units
            });
        }

        return summary;
    }
}
