using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private const int MaxIdempotencyKeyLength = 200;

    private readonly PaymentStore _store;
    private readonly FinixPaymentGateway _gateway;
    private readonly FinixOptions _options;
    private readonly IUriComposer _uriComposer;

    public PaymentService(
        PaymentStore store,
        FinixPaymentGateway gateway,
        FinixOptions options,
        IUriComposer uriComposer)
    {
        _store = store;
        _gateway = gateway;
        _options = options;
        _uriComposer = uriComposer;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw PaymentException.BadRequest("ORDER_ITEMS_REQUIRED", "The order must contain at least one catalog item.");

        var lines = request.Items
            .Where(i => i.CatalogItemId > 0 && i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineRequest { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (lines.Count == 0)
            throw PaymentException.BadRequest("ORDER_ITEMS_INVALID", "Every order line needs a catalog item id and a positive quantity.");

        var catalogItems = await _store.GetCatalogItemsAsync(
            lines.Select(l => l.CatalogItemId).ToArray(),
            cancellationToken);

        if (catalogItems.Length != lines.Count)
        {
            var found = catalogItems.Select(i => i.Id).ToHashSet();
            var missing = lines.Where(l => !found.Contains(l.CatalogItemId)).Select(l => l.CatalogItemId);
            throw PaymentException.BadRequest(
                "UNKNOWN_CATALOG_ITEM",
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var address = BuildAddress(request.ShipToAddress);

        var orderItems = lines.Select(line =>
        {
            var item = catalogItems.First(i => i.Id == line.CatalogItemId);
            var ordered = new CatalogItemOrdered(
                item.Id,
                item.Name,
                _uriComposer.ComposePicUri(item.PictureUri));
            return new OrderItem(ordered, item.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, address, orderItems);
        await _store.AddOrderAsync(order, cancellationToken);

        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
            throw PaymentException.BadRequest("ORDER_TOTAL_INVALID", "The order total must be greater than zero.");

        var now = DateTimeOffset.UtcNow;
        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = buyerId,
            PaymentReference = Guid.NewGuid().ToString("N"),
            Status = PaymentStatus.AwaitingPayment,
            AmountCents = amountCents,
            Currency = _options.NormalizedCurrency,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _store.AddOrderPaymentAsync(payment, cancellationToken);

        return new CreateOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            AmountCents = amountCents,
            Currency = payment.Currency,
            Status = payment.Status.ToString()
        };
    }

    public async Task<PayOrderResponse> PayAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _store.GetOrderWithItemsAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw PaymentException.NotFound("ORDER_NOT_FOUND", "No order with that id belongs to the caller.");

        var payment = await _store.GetPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            var now = DateTimeOffset.UtcNow;
            payment = new OrderPayment
            {
                OrderId = orderId,
                BuyerId = buyerId,
                PaymentReference = Guid.NewGuid().ToString("N"),
                Status = PaymentStatus.AwaitingPayment,
                AmountCents = ToCents(order.Total()),
                Currency = _options.NormalizedCurrency,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _store.AddOrderPaymentAsync(payment, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(payment.PaymentReference))
        {
            payment.PaymentReference = Guid.NewGuid().ToString("N");
            await _store.SaveAsync(cancellationToken);
        }

        if (payment.Status == PaymentStatus.Authorized)
            return MapPay(payment);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw PaymentException.Conflict("ORDER_ALREADY_CAPTURED", "This order has already been paid and captured. Use the refunds endpoint to return money.");

        if (payment.Status == PaymentStatus.Cancelled)
            throw PaymentException.Conflict("ORDER_CANCELLED", "This order was cancelled and cannot be paid.");

        if (payment.Status == PaymentStatus.Unknown)
            throw PaymentException.Conflict("PAYMENT_OUTCOME_UNKNOWN",
                "A previous payment attempt has an unknown outcome. Reconcile the payment with Finix before retrying.");

        var hasSavedCard = !string.IsNullOrWhiteSpace(request.SavedCardId);
        var hasCard = request.Card is not null;
        if (hasSavedCard == hasCard)
            throw PaymentException.BadRequest("PAYMENT_METHOD_REQUIRED",
                "Provide exactly one of savedCardId or card.");

        string? savedInstrumentId = null;
        CardDetails? oneOffCard = null;

        if (hasSavedCard)
        {
            var saved = await _store.GetSavedMethodAsync(buyerId, request.SavedCardId!, cancellationToken);
            if (saved is null || !saved.Enabled)
                throw PaymentException.NotFound("PAYMENT_METHOD_NOT_FOUND", "No saved card with that id belongs to the caller.");
            savedInstrumentId = saved.FinixPaymentInstrumentId;
        }
        else
        {
            oneOffCard = ValidateCard(request.Card!, buyerId);
        }

        if (!await _store.TryClaimOperationAsync(orderId, "pay", cancellationToken))
        {
            var current = await _store.GetPaymentAsync(orderId, cancellationToken);
            if (current?.Status == PaymentStatus.Authorized)
                return MapPay(current);
            throw PaymentException.Conflict("PAYMENT_IN_PROGRESS", "A payment attempt for this order is already in progress.");
        }

        var attempt = payment.AttemptNo + 1;

        try
        {
            var identityId = await EnsureIdentityAsync(buyerId, cancellationToken);
            var source = savedInstrumentId;
            if (source is null)
            {
                var instrument = await _gateway.CreateInstrumentAsync(identityId, oneOffCard!, cancellationToken);
                source = instrument.Id;
            }

            var authorization = await _gateway.AuthorizeAsync(
                payment.PaymentReference,
                attempt,
                source,
                payment.AmountCents,
                _options.ResolveCurrency(),
                cancellationToken);

            payment.AttemptNo = attempt;
            payment.AuthorizationId = authorization.Id;
            payment.AuthorizationState = authorization.State;
            payment.AuthorizationIdempotencyId = $"eshop-{payment.PaymentReference}-pay-{attempt}";
            payment.AuthorizationExpiresAt = authorization.ExpiresAt;
            payment.UpdatedAt = DateTimeOffset.UtcNow;

            if (string.Equals(authorization.State, "FAILED", StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(authorization.FailureCode))
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = authorization.FailureCode ?? "PAYMENT_DECLINED";
                payment.FailureMessage = authorization.FailureMessage ?? "Finix declined the authorization.";
                await _store.SaveAsync(cancellationToken);
                await _store.ReleaseOperationClaimAsync(orderId, "pay", cancellationToken);
                throw new PaymentException(402, payment.FailureCode!, payment.FailureMessage!);
            }

            payment.Status = PaymentStatus.Authorized;
            payment.FailureCode = null;
            payment.FailureMessage = null;
            await _store.SaveAsync(cancellationToken);

            return MapPay(payment);
        }
        catch (PaymentException ex)
        {
            var providerFailure = ex.UnknownOutcome ||
                                  ex.StatusCode == 402 ||
                                  ex.Code.StartsWith("PROVIDER_", StringComparison.Ordinal);

            if (!providerFailure)
            {
                await _store.ReleaseOperationClaimAsync(orderId, "pay", cancellationToken);
                throw;
            }

            payment.AttemptNo = attempt;
            payment.FailureCode = ex.Code;
            payment.FailureMessage = ex.Message;
            payment.UpdatedAt = DateTimeOffset.UtcNow;

            if (ex.UnknownOutcome)
            {
                payment.Status = PaymentStatus.Unknown;
                await _store.SaveAsync(cancellationToken);
                // Keep the claim: an authorization whose outcome is unknown must not be retried blindly.
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                await _store.SaveAsync(cancellationToken);
                await _store.ReleaseOperationClaimAsync(orderId, "pay", cancellationToken);
            }

            throw;
        }
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _store.GetPaymentAsync(orderId, cancellationToken);
        if (payment is null)
            throw PaymentException.NotFound("ORDER_PAYMENT_NOT_FOUND", "No payment exists for that order.");

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            await RefreshFeesIfNeededAsync(payment, cancellationToken);
            return MapFulfil(payment, payment.FeeComplete);
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
            throw PaymentException.Conflict("ORDER_NOT_AUTHORIZED", "The order is not authorized, so it cannot be fulfilled.");

        if (!await _store.TryClaimOperationAsync(orderId, "fulfil", cancellationToken))
        {
            var current = await _store.GetPaymentAsync(orderId, cancellationToken);
            if (current?.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                await RefreshFeesIfNeededAsync(current, cancellationToken);
                return MapFulfil(current, current.FeeComplete);
            }
            throw PaymentException.Conflict("FULFIL_IN_PROGRESS", "A fulfilment attempt for this order is already in progress.");
        }

        try
        {
            var capture = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.AmountCents, cancellationToken);
            if (capture.CapturedAmountCents <= 0)
                throw new PaymentException(409, "AUTHORIZATION_NOT_CAPTURABLE",
                    $"Finix did not report a captured amount for authorization {payment.AuthorizationId}.");

            payment.Status = PaymentStatus.Captured;
            payment.AuthorizationState = capture.AuthorizationState ?? payment.AuthorizationState;
            payment.CaptureTransferId = capture.TransferId;
            payment.CaptureState = capture.TransferState;
            payment.CapturedAmountCents = capture.CapturedAmountCents;
            payment.FeeAmountCents = capture.FeeAmountCents;
            payment.NetAmountCents = capture.NetAmountCents;
            payment.FeeComplete = capture.FeeAmountCents > 0 && !capture.FeeTruncated;
            payment.FailureCode = null;
            payment.FailureMessage = null;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveAsync(cancellationToken);

            return MapFulfil(payment, payment.FeeComplete);
        }
        catch (PaymentException ex)
        {
            if (ex.UnknownOutcome)
            {
                payment.Status = PaymentStatus.Unknown;
                payment.FailureCode = ex.Code;
                payment.FailureMessage = ex.Message;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(cancellationToken);
                // Keep the claim: a capture whose outcome is unknown must not be retried blindly.
                throw;
            }

            AuthorizationInfo? authorization = null;
            try
            {
                authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            }
            catch (PaymentException)
            {
                // The operator message falls back to the provider failure below.
            }

            var message = BuildCaptureFailureMessage(payment.AuthorizationId!, authorization, ex);
            payment.FailureCode = "AUTHORIZATION_NOT_CAPTURABLE";
            payment.FailureMessage = message;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveAsync(cancellationToken);
            await _store.ReleaseOperationClaimAsync(orderId, "fulfil", cancellationToken);

            throw new PaymentException(409, "AUTHORIZATION_NOT_CAPTURABLE", message, ex);
        }
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _store.GetPaymentAsync(orderId, cancellationToken);
        if (payment is null)
            throw PaymentException.NotFound("ORDER_PAYMENT_NOT_FOUND", "No payment exists for that order.");

        if (payment.Status == PaymentStatus.Cancelled)
            return MapCancel(payment);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw PaymentException.Conflict("ORDER_ALREADY_CAPTURED",
                "This order has already been captured. Use the refunds endpoint to return money.");

        if (payment.Status is PaymentStatus.AwaitingPayment or PaymentStatus.Failed ||
            string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            payment.Status = PaymentStatus.Cancelled;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveAsync(cancellationToken);
            return MapCancel(payment);
        }

        if (!await _store.TryClaimOperationAsync(orderId, "cancel", cancellationToken))
        {
            var current = await _store.GetPaymentAsync(orderId, cancellationToken);
            if (current?.Status == PaymentStatus.Cancelled)
                return MapCancel(current);
            throw PaymentException.Conflict("CANCEL_IN_PROGRESS", "A cancellation attempt for this order is already in progress.");
        }

        try
        {
            var voidInfo = await _gateway.VoidAsync(payment.AuthorizationId!, cancellationToken);
            payment.Status = PaymentStatus.Cancelled;
            payment.AuthorizationState = voidInfo.State ?? payment.AuthorizationState;
            payment.FailureCode = null;
            payment.FailureMessage = null;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveAsync(cancellationToken);

            var response = MapCancel(payment);
            response.VoidState = voidInfo.VoidState;
            return response;
        }
        catch (PaymentException ex)
        {
            if (ex.UnknownOutcome)
            {
                payment.Status = PaymentStatus.Unknown;
                payment.FailureCode = ex.Code;
                payment.FailureMessage = ex.Message;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(cancellationToken);
                // Keep the claim: a void whose outcome is unknown must not be retried blindly.
                throw;
            }

            await _store.ReleaseOperationClaimAsync(orderId, "cancel", cancellationToken);
            throw;
        }
    }

    public async Task<RefundResponse> RefundAsync(
        string buyerId,
        int orderId,
        RefundRequest request,
        CancellationToken cancellationToken)
    {
        var payment = await _store.GetPaymentAsync(orderId, cancellationToken);
        if (payment is null || !string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw PaymentException.NotFound("ORDER_NOT_FOUND", "No order with that id belongs to the caller.");

        if (payment.Status == PaymentStatus.Refunded)
            throw PaymentException.Conflict("ORDER_FULLY_REFUNDED", "The captured payment has already been refunded in full.");

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) ||
            string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw PaymentException.Conflict("ORDER_NOT_CAPTURED", "Only a captured payment can be refunded.");
        }

        var key = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw PaymentException.BadRequest("IDEMPOTENCY_KEY_REQUIRED", "A refund idempotency key is required.");
        if (key.Length > MaxIdempotencyKeyLength)
            throw PaymentException.BadRequest("IDEMPOTENCY_KEY_TOO_LONG",
                $"The refund idempotency key must be at most {MaxIdempotencyKeyLength} characters.");

        var existing = await _store.GetRefundAsync(orderId, key, cancellationToken);
        if (existing is not null)
            return MapExistingRefund(existing, payment);

        var captureTransfer = await _gateway.GetTransferAsync(payment.CaptureTransferId!, cancellationToken);
        if (string.Equals(captureTransfer.State, "PENDING", StringComparison.Ordinal))
        {
            throw new PaymentException(409, "CAPTURE_PENDING",
                $"Capture transfer {payment.CaptureTransferId} is still pending at Finix. " +
                "Retry the refund once the capture has succeeded.");
        }
        if (string.Equals(captureTransfer.State, "FAILED", StringComparison.Ordinal) ||
            string.Equals(captureTransfer.State, "CANCELED", StringComparison.Ordinal) ||
            string.Equals(captureTransfer.State, "RETURNED", StringComparison.Ordinal))
        {
            throw new PaymentException(409, "CAPTURE_NOT_REFUNDABLE",
                $"Capture transfer {payment.CaptureTransferId} is {captureTransfer.State} and cannot be refunded.");
        }

        var storedRefunds = await _store.GetRefundsAsync(orderId, cancellationToken);
        var storedRefunded = storedRefunds
            .Where(r => !string.IsNullOrWhiteSpace(r.FinixReversalId) &&
                        !string.Equals(r.State, "FAILED", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(r.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.AmountCents);

        var finixRefunded = await _gateway.GetRefundedAsync(payment.CaptureTransferId!, cancellationToken);
        if (finixRefunded.Truncated)
        {
            throw new PaymentException(502, "REFUND_LEDGER_INCOMPLETE",
                $"Finix returned more refund pages than this service can verify for transfer {payment.CaptureTransferId} " +
                $"(ledger {finixRefunded.Cents} cents). Refusing to refund without a complete ledger.");
        }

        var refunded = Math.Max(storedRefunded, finixRefunded.Cents);
        var refundable = payment.CapturedAmountCents - refunded;
        if (refundable <= 0)
            throw PaymentException.Conflict("ORDER_FULLY_REFUNDED", "The captured payment has already been refunded in full.");

        var amount = request.AmountCents ?? refundable;
        if (amount <= 0)
            throw PaymentException.BadRequest("INVALID_REFUND_AMOUNT", "The refund amount must be greater than zero.");
        if (amount > refundable)
        {
            throw PaymentException.BadRequest("REFUND_EXCEEDS_CAPTURE",
                $"The refund amount {amount} cents exceeds the refundable balance of {refundable} cents.");
        }

        var now = DateTimeOffset.UtcNow;
        var claim = new OrderRefund
        {
            OrderId = orderId,
            IdempotencyKey = key,
            RefundId = Guid.NewGuid().ToString("N"),
            AmountCents = amount,
            CreatedAt = now
        };

        if (!await _store.TryClaimRefundAsync(claim, cancellationToken))
        {
            var current = await _store.GetRefundAsync(orderId, key, cancellationToken);
            if (current?.FinixReversalId is not null)
                return MapExistingRefund(current, payment);
            throw PaymentException.Conflict("REFUND_IN_PROGRESS",
                "A refund with this idempotency key is already in progress or its outcome is unknown. Reconcile before retrying.");
        }

        try
        {
            var refund = await _gateway.RefundAsync(
                payment.CaptureTransferId!,
                amount,
                $"eshop-refund-{payment.PaymentReference}-{key}",
                cancellationToken);
            var newRefunded = refunded + amount;
            var status = newRefunded >= payment.CapturedAmountCents
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;

            await _store.CompleteRefundAsync(
                orderId,
                key,
                refund.ReversalId,
                refund.State,
                newRefunded,
                status,
                cancellationToken);

            payment.RefundedAmountCents = newRefunded;
            payment.Status = status;
            payment.UpdatedAt = DateTimeOffset.UtcNow;

            return new RefundResponse
            {
                OrderId = orderId,
                RefundId = claim.RefundId,
                FinixReversalId = refund.ReversalId,
                AmountCents = amount,
                RefundedAmountCents = newRefunded,
                RefundableAmountCents = Math.Max(0, payment.CapturedAmountCents - newRefunded),
                Currency = payment.Currency,
                Status = status.ToString()
            };
        }
        catch (PaymentException ex)
        {
            if (ex.UnknownOutcome)
            {
                await _store.MarkRefundUnknownAsync(orderId, key, cancellationToken);
                // Keep the claim: a refund whose outcome is unknown must not be retried blindly.
            }
            else
            {
                await _store.ReleaseRefundClaimAsync(orderId, key, cancellationToken);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<MyOrderResponse>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _store.GetOrdersForBuyerAsync(buyerId, cancellationToken);
        var responses = new List<MyOrderResponse>(orders.Count);

        foreach (var order in orders)
        {
            var payment = await _store.GetPaymentAsync(order.Id, cancellationToken);
            var total = order.Total();
            var response = new MyOrderResponse
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = total,
                Currency = payment?.Currency ?? _options.NormalizedCurrency,
                Status = (payment?.Status ?? PaymentStatus.AwaitingPayment).ToString(),
                AmountCents = payment?.AmountCents ?? ToCents(total),
                CapturedAmountCents = payment?.CapturedAmountCents ?? 0,
                RefundedAmountCents = payment?.RefundedAmountCents ?? 0,
                RefundableAmountCents = payment is null
                    ? 0
                    : Math.Max(0, payment.CapturedAmountCents - payment.RefundedAmountCents),
                AuthorizationId = payment?.AuthorizationId,
                CaptureTransferId = payment?.CaptureTransferId,
                Items = order.OrderItems.Select(i => new MyOrderItemResponse
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList()
            };

            responses.Add(response);
        }

        return responses;
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var cardRequest = new CardRequest
        {
            Number = request.Number,
            ExpirationMonth = request.ExpirationMonth,
            ExpirationYear = request.ExpirationYear,
            SecurityCode = request.SecurityCode,
            Name = request.Name
        };
        var card = ValidateCard(cardRequest, buyerId);
        var fingerprint = Fingerprint(buyerId, card);

        if (!await _store.TryClaimSavedCardAsync(buyerId, fingerprint, cancellationToken))
        {
            var claim = await _store.GetSavedCardClaimAsync(buyerId, fingerprint, cancellationToken);
            if (!string.IsNullOrWhiteSpace(claim?.PaymentMethodId))
            {
                var existing = await _store.GetSavedMethodAsync(buyerId, claim.PaymentMethodId!, cancellationToken);
                if (existing is not null)
                    return MapSaved(existing);
            }

            throw PaymentException.Conflict("CARD_SAVE_IN_PROGRESS", "This card is already being saved for the caller.");
        }

        try
        {
            var identityId = await EnsureIdentityAsync(buyerId, cancellationToken);
            var instrument = await _gateway.CreateInstrumentAsync(identityId, card, cancellationToken);

            var method = new SavedPaymentMethod
            {
                BuyerId = buyerId,
                CardFingerprint = fingerprint,
                Id = Guid.NewGuid().ToString("N"),
                FinixPaymentInstrumentId = instrument.Id,
                Brand = instrument.Brand ?? "UNKNOWN",
                LastFour = instrument.LastFour ?? LastFour(card.Number),
                ExpirationMonth = instrument.ExpirationMonth ?? card.ExpirationMonth,
                ExpirationYear = instrument.ExpirationYear ?? card.ExpirationYear,
                Enabled = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var saved = await _store.CompleteSavedCardAsync(method, buyerId, fingerprint, cancellationToken);
            if (saved is null)
                throw new PaymentException(502, "CARD_SAVE_FAILED", "The card was saved with Finix but not recorded locally.");

            return MapSaved(saved);
        }
        catch
        {
            await _store.ReleaseSavedCardClaimAsync(buyerId, fingerprint, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var methods = await _store.GetSavedMethodsAsync(buyerId, cancellationToken);
        return methods.Select(m => new PaymentMethodResponse
        {
            PaymentMethodId = m.Id,
            Brand = m.Brand,
            LastFour = m.LastFour,
            ExpirationMonth = m.ExpirationMonth,
            ExpirationYear = m.ExpirationYear,
            CreatedAt = m.CreatedAt
        }).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _store.GetSavedMethodAsync(buyerId, paymentMethodId, cancellationToken);
        if (method is null || !method.Enabled)
            throw PaymentException.NotFound("PAYMENT_METHOD_NOT_FOUND", "No saved card with that id belongs to the caller.");

        try
        {
            await _gateway.DisableInstrumentAsync(method.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // The instrument is already gone at Finix; the local soft delete is still correct.
        }

        method.Enabled = false;
        await _store.SaveAsync(cancellationToken);
        await _store.RemoveSavedCardClaimAsync(method.BuyerId, method.CardFingerprint, cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw PaymentException.BadRequest("INVALID_DATE_RANGE", "'from' must be before or equal to 'to'.");

        var finix = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _store.GetPaymentsInRangeAsync(from, to, cancellationToken);
        var refunds = await _store.GetRefundsInRangeAsync(from, to, cancellationToken);

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            Truncated = finix.Truncated
        };

        var authorizationIds = new HashSet<string>(StringComparer.Ordinal);
        var transferIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transaction in finix.Transactions)
        {
            if (string.Equals(transaction.Kind, "authorization", StringComparison.Ordinal))
                authorizationIds.Add(transaction.Id);
            else
                transferIds.Add(transaction.Id);

            int? matchedOrderId = null;

            if (string.Equals(transaction.Kind, "authorization", StringComparison.Ordinal))
            {
                matchedOrderId = payments
                    .FirstOrDefault(p => string.Equals(p.AuthorizationId, transaction.Id, StringComparison.Ordinal))
                    ?.OrderId;
            }
            else
            {
                matchedOrderId = payments
                    .FirstOrDefault(p => string.Equals(p.CaptureTransferId, transaction.Id, StringComparison.Ordinal))
                    ?.OrderId;

                matchedOrderId ??= refunds
                    .FirstOrDefault(r => string.Equals(r.FinixReversalId, transaction.Id, StringComparison.Ordinal))
                    ?.OrderId;
            }

            var dto = new ReconciliationTransactionResponse
            {
                Id = transaction.Id,
                Kind = transaction.Kind,
                Type = transaction.Type,
                State = transaction.State,
                AmountCents = transaction.AmountCents,
                Currency = transaction.Currency,
                CreatedAt = transaction.CreatedAt,
                IdempotencyId = transaction.IdempotencyId,
                ParentTransferId = transaction.ParentTransferId,
                MatchedOrderId = matchedOrderId
            };

            response.FinixTransactions.Add(dto);
            if (matchedOrderId is null)
                response.UnmatchedFinix.Add(dto);
        }

        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId) &&
                !authorizationIds.Contains(payment.AuthorizationId!))
            {
                response.UnmatchedEshop.Add(new UnmatchedEshopRecordResponse
                {
                    OrderId = payment.OrderId,
                    Kind = "authorization",
                    Reference = payment.AuthorizationId!,
                    Status = payment.Status.ToString(),
                    AmountCents = payment.AmountCents,
                    UpdatedAt = payment.UpdatedAt
                });
            }

            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId) &&
                !transferIds.Contains(payment.CaptureTransferId!))
            {
                response.UnmatchedEshop.Add(new UnmatchedEshopRecordResponse
                {
                    OrderId = payment.OrderId,
                    Kind = "capture",
                    Reference = payment.CaptureTransferId!,
                    Status = payment.Status.ToString(),
                    AmountCents = payment.CapturedAmountCents,
                    UpdatedAt = payment.UpdatedAt
                });
            }
        }

        foreach (var refund in refunds)
        {
            if (!string.IsNullOrWhiteSpace(refund.FinixReversalId) &&
                !transferIds.Contains(refund.FinixReversalId!))
            {
                response.UnmatchedEshop.Add(new UnmatchedEshopRecordResponse
                {
                    OrderId = refund.OrderId,
                    Kind = "refund",
                    Reference = refund.FinixReversalId!,
                    Status = refund.State ?? "UNKNOWN",
                    AmountCents = refund.AmountCents,
                    UpdatedAt = refund.CreatedAt
                });
            }
        }

        return response;
    }

    private async Task RefreshFeesIfNeededAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (payment.FeeComplete || string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            return;

        try
        {
            var fees = await _gateway.GetFeesAsync(payment.CaptureTransferId!, cancellationToken);
            if (fees.Cents <= 0)
                return;

            payment.FeeAmountCents = fees.Cents;
            payment.NetAmountCents = payment.CapturedAmountCents - fees.Cents;
            payment.FeeComplete = !fees.Truncated;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _store.SaveAsync(cancellationToken);
        }
        catch (PaymentException)
        {
            // Leave the fee incomplete; a later fulfilment attempt can refresh it.
        }
    }

    private async Task<string> EnsureIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _store.GetIdentityAsync(buyerId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing?.FinixIdentityId))
            return existing!.FinixIdentityId!;

        if (!await _store.TryClaimIdentityAsync(buyerId, cancellationToken))
        {
            existing = await _store.GetIdentityAsync(buyerId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(existing?.FinixIdentityId))
                return existing!.FinixIdentityId!;
            throw PaymentException.Conflict("IDENTITY_IN_PROGRESS", "The shopper's Finix identity is already being created.");
        }

        try
        {
            var identityId = await _gateway.CreateIdentityAsync(buyerId, cancellationToken);
            await _store.CompleteIdentityAsync(buyerId, identityId, cancellationToken);
            return identityId;
        }
        catch
        {
            await _store.ReleaseIdentityClaimAsync(buyerId, cancellationToken);
            throw;
        }
    }

    private static CardDetails ValidateCard(CardRequest request, string buyerId)
    {
        var number = new string((request.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (number.Length is < 12 or > 19)
            throw PaymentException.BadRequest("INVALID_CARD_NUMBER", "The card number must contain 12 to 19 digits.");

        if (request.ExpirationMonth is < 1 or > 12)
            throw PaymentException.BadRequest("INVALID_CARD_EXPIRY", "The expiration month must be between 1 and 12.");

        if (request.ExpirationYear is < 1000 or > 9999)
            throw PaymentException.BadRequest("INVALID_CARD_EXPIRY", "The expiration year must be a 4-digit year.");

        var today = DateTimeOffset.UtcNow;
        if (request.ExpirationYear < today.Year ||
            (request.ExpirationYear == today.Year && request.ExpirationMonth < today.Month))
        {
            throw PaymentException.BadRequest("CARD_EXPIRED", "The card expiration date is in the past.");
        }

        var securityCode = request.SecurityCode?.Trim();
        if (!string.IsNullOrEmpty(securityCode) &&
            (securityCode.Length is < 3 or > 4 || !securityCode.All(char.IsDigit)))
        {
            throw PaymentException.BadRequest("INVALID_SECURITY_CODE", "The security code must be 3 or 4 digits.");
        }

        return new CardDetails(
            number,
            request.ExpirationMonth,
            request.ExpirationYear,
            string.IsNullOrEmpty(securityCode) ? null : securityCode,
            string.IsNullOrWhiteSpace(request.Name) ? buyerId : request.Name!.Trim());
    }

    private static string Fingerprint(string buyerId, CardDetails card)
    {
        var raw = $"{buyerId}|{card.Number}|{card.ExpirationMonth}|{card.ExpirationYear}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Address BuildAddress(AddressRequest? request)
    {
        if (request is null)
            throw PaymentException.BadRequest("SHIP_TO_ADDRESS_REQUIRED", "A shipping address is required.");

        var street = request.Street?.Trim();
        var city = request.City?.Trim();
        var country = request.Country?.Trim();
        var zip = request.ZipCode?.Trim();

        if (string.IsNullOrWhiteSpace(street) ||
            string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(country) ||
            string.IsNullOrWhiteSpace(zip))
        {
            throw PaymentException.BadRequest("SHIP_TO_ADDRESS_INVALID",
                "The shipping address needs street, city, country and zip code.");
        }

        return new Address(street!, city!, request.State?.Trim() ?? string.Empty, country!, zip!);
    }

    private static long ToCents(decimal dollars) =>
        (long)decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero);

    private static string LastFour(string number) =>
        number.Length <= 4 ? number : number[^4..];

    private static string BuildCaptureFailureMessage(
        string authorizationId,
        AuthorizationInfo? authorization,
        PaymentException failure)
    {
        if (authorization is null)
        {
            return $"Authorization {authorizationId} could not be captured ({failure.Code}). " +
                   "Check the authorization in Finix, then cancel the order and ask the shopper to pay again.";
        }

        if (authorization.IsVoid ||
            string.Equals(authorization.State, "CANCELED", StringComparison.Ordinal) ||
            string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.Ordinal))
        {
            return $"Authorization {authorizationId} was voided and can no longer be captured. " +
                   "Cancel the order; the shopper must place a new order and pay again.";
        }

        if (authorization.ExpiresAt is not null && authorization.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return $"Authorization {authorizationId} expired at {authorization.ExpiresAt:O} and can no longer be captured. " +
                   "Cancel the order and ask the shopper to pay again.";
        }

        if (!string.IsNullOrWhiteSpace(authorization.FailureCode))
        {
            return $"Authorization {authorizationId} cannot be captured: " +
                   $"{authorization.FailureCode} - {authorization.FailureMessage}. " +
                   "Cancel the order and ask the shopper to pay again.";
        }

        return $"Authorization {authorizationId} could not be captured " +
               $"(state {authorization.State ?? "unknown"}, void state {authorization.VoidState ?? "unknown"}, " +
               $"provider code {failure.Code}). Cancel the order and ask the shopper to pay again.";
    }

    private static PayOrderResponse MapPay(OrderPayment payment) =>
        Fill(new PayOrderResponse(), payment);

    private static FulfilOrderResponse MapFulfil(OrderPayment payment, bool feeComplete) =>
        Fill(new FulfilOrderResponse { FeeComplete = feeComplete }, payment);

    private static CancelOrderResponse MapCancel(OrderPayment payment) =>
        Fill(new CancelOrderResponse(), payment);

    private static T Fill<T>(T response, OrderPayment payment) where T : PaymentSummaryResponse
    {
        response.OrderId = payment.OrderId;
        response.Status = payment.Status.ToString();
        response.AmountCents = payment.AmountCents;
        response.Currency = payment.Currency;
        response.AuthorizationId = payment.AuthorizationId;
        response.AuthorizationState = payment.AuthorizationState;
        response.CaptureTransferId = payment.CaptureTransferId;
        response.CaptureState = payment.CaptureState;
        response.CapturedAmountCents = payment.CapturedAmountCents;
        response.FeeAmountCents = payment.FeeAmountCents;
        response.NetAmountCents = payment.NetAmountCents;
        response.RefundedAmountCents = payment.RefundedAmountCents;
        response.RefundableAmountCents = Math.Max(0, payment.CapturedAmountCents - payment.RefundedAmountCents);
        response.FailureCode = payment.FailureCode;
        response.FailureMessage = payment.FailureMessage;
        response.UpdatedAt = payment.UpdatedAt;
        return response;
    }

    private static SavePaymentMethodResponse MapSaved(SavedPaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            LastFour = method.LastFour,
            ExpirationMonth = method.ExpirationMonth,
            ExpirationYear = method.ExpirationYear
        };

    private static RefundResponse MapExistingRefund(OrderRefund refund, OrderPayment payment)
    {
        if (string.IsNullOrWhiteSpace(refund.FinixReversalId))
        {
            throw PaymentException.Conflict("REFUND_IN_PROGRESS",
                "A refund with this idempotency key is already in progress or its outcome is unknown. Reconcile before retrying.");
        }

        return new RefundResponse
        {
            OrderId = refund.OrderId,
            RefundId = refund.RefundId,
            FinixReversalId = refund.FinixReversalId,
            AmountCents = refund.AmountCents,
            RefundedAmountCents = payment.RefundedAmountCents,
            RefundableAmountCents = Math.Max(0, payment.CapturedAmountCents - payment.RefundedAmountCents),
            Currency = payment.Currency,
            Status = payment.Status.ToString()
        };
    }
}
