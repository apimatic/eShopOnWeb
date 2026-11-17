using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuyerLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IRepository<FinixCustomer> _finixCustomerRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IFinixClient _finixClient;
    private readonly IOptions<FinixOptions> _finixOptions;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedCard> savedCardRepository,
        IRepository<FinixCustomer> finixCustomerRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IFinixClient finixClient,
        IOptions<FinixOptions> finixOptions,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _finixCustomerRepository = finixCustomerRepository;
        _catalogItemRepository = catalogItemRepository;
        _finixClient = finixClient;
        _finixOptions = finixOptions;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required.");
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one order item is required.");
        }

        if (command.Items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw new PaymentOperationException(400, "Every order item needs a positive catalog item id and quantity.");
        }

        var catalogItemIds = command.Items.Select(item => item.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogItemsById = catalogItems.ToDictionary(item => item.Id);

        if (catalogItemIds.Any(id => !catalogItemsById.ContainsKey(id)))
        {
            throw new PaymentOperationException(400, "One or more catalog items do not exist.");
        }

        var orderItems = command.Items.Select(item =>
        {
            var catalogItem = catalogItemsById[item.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = ToAddress(command.ShippingAddress);
        var order = new Order(buyerId, address, orderItems);
        order.InitializePayment(_finixOptions.Value.NormalizedCurrency);

        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Created order {OrderId} for buyer {BuyerId} with total {Total} {Currency}", order.Id, buyerId, order.Total(), order.Currency);

        return new PlaceOrderResult(order.Id, order.Total(), order.Currency, order.PaymentStatus.ToString());
    }

    public async Task<PaymentResult> PayOrderAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

            if (order.PaymentStatus is PaymentStatus.Authorized or PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return new PaymentResult(
                    order.Id,
                    order.PaymentStatus.ToString(),
                    order.FinixAuthorizationId,
                    order.FinixAuthorizationState,
                    order.FinixPaymentInstrumentId,
                    order.Total(),
                    order.Currency,
                    true);
            }

            if (order.PaymentStatus == PaymentStatus.Canceled)
            {
                throw new PaymentOperationException(409, "The order was canceled and can no longer be paid.");
            }

            if (order.PaymentStatus is not (PaymentStatus.AwaitingPayment or PaymentStatus.Failed))
            {
                throw new PaymentOperationException(409, $"The order cannot be paid while it is {order.PaymentStatus}.");
            }

            var paymentInstrument = await ResolvePaymentInstrumentAsync(buyerId, command, cancellationToken);
            var amountCents = ToCents(order.Total());

            var request = new FinixCreateAuthorizationRequest
            {
                Amount = amountCents,
                Currency = order.Currency,
                Merchant = _finixOptions.Value.MerchantId,
                Source = paymentInstrument.PaymentInstrumentId,
                IdempotencyId = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                    ? Guid.NewGuid().ToString("N")
                    : command.IdempotencyKey,
                StatementDescriptor = "ESHOP ORDER",
                Tags = new Dictionary<string, string>
                {
                    ["eshop_order_id"] = order.Id.ToString(),
                    ["eshop_payment_reference"] = order.PaymentReference,
                    ["eshop_buyer_id"] = buyerId
                }
            };

            FinixAuthorizationResponse authorization;
            try
            {
                authorization = await _finixClient.CreateAuthorizationAsync(request, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                order.MarkAuthorizationFailed("FAILED", ex.Message);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(402, $"The card authorization failed: {ex.Message}");
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                var message = authorization.FailureMessage ?? $"Finix returned authorization state {authorization.State}.";
                order.MarkAuthorizationFailed(authorization.State, message);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(402, $"The card authorization was not successful: {message}");
            }

            order.MarkAuthorized(
                authorization.Id,
                authorization.State,
                paymentInstrument.PaymentInstrumentId,
                paymentInstrument.SavedCardId);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Authorized order {OrderId} with Finix authorization {AuthorizationId}", order.Id, authorization.Id);

            return new PaymentResult(
                order.Id,
                order.PaymentStatus.ToString(),
                authorization.Id,
                authorization.State,
                paymentInstrument.PaymentInstrumentId,
                order.Total(),
                order.Currency,
                false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FulfilmentResult> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.PaymentStatus is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return ToFulfilmentResult(order, true);
            }

            if (order.PaymentStatus != PaymentStatus.Authorized)
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} cannot be fulfilled while its payment is {order.PaymentStatus}. " +
                    "An operator should authorize payment first, or cancel the order if payment will not be collected.");
            }

            if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has no Finix authorization id. Re-authorize the order before fulfilment.");
            }

            FinixAuthorizationResponse authorization;
            try
            {
                authorization = await _finixClient.GetAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(409,
                    $"Finix could not confirm authorization {order.FinixAuthorizationId} for order {order.Id}: {ex.Message}. " +
                    "Re-authorize the order or cancel it.");
            }

            if (authorization.IsVoid)
            {
                throw new PaymentOperationException(409,
                    $"Authorization {authorization.Id} for order {order.Id} was voided and cannot be captured. " +
                    "Re-authorize the order before fulfilment.");
            }

            if (!string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                var existingTransfer = await WaitForTransferSettlementAsync(authorization.Transfer, cancellationToken);
                if (IsFailedTransferState(existingTransfer.State))
                {
                    order.UpdateTransferState(existingTransfer.State);
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    throw new PaymentOperationException(409,
                        $"Finix capture transfer {existingTransfer.Id} for order {order.Id} ended in {existingTransfer.State}: " +
                        $"{existingTransfer.FailureCode} {existingTransfer.FailureMessage}. Re-authorize the order before fulfilment.");
                }

                var existingFeeCents = await GetFinixFeeCentsAsync(existingTransfer.Id, cancellationToken);
                order.MarkCaptured(
                    existingTransfer.Id,
                    existingTransfer.State,
                    existingTransfer.Amount,
                    existingFeeCents,
                    existingTransfer.Amount - existingFeeCents);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return ToFulfilmentResult(order, true);
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"Authorization {authorization.Id} for order {order.Id} is {authorization.State} and cannot be captured. " +
                    "Re-authorize the order before fulfilment.");
            }

            if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            {
                throw new PaymentOperationException(409,
                    $"Authorization {authorization.Id} for order {order.Id} expired at {authorization.ExpiresAt:O}. " +
                    "Re-authorize the order before fulfilment.");
            }

            var captureAmountCents = ToCents(order.Total());
            FinixAuthorizationResponse captured;
            try
            {
                captured = await _finixClient.CaptureAuthorizationAsync(authorization.Id, captureAmountCents, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(409,
                    $"Finix could not capture authorization {authorization.Id} for order {order.Id}: {ex.Message}. " +
                    "Re-authorize the order or cancel it.");
            }

            if (string.IsNullOrWhiteSpace(captured.Transfer))
            {
                throw new PaymentOperationException(409,
                    $"Finix captured authorization {authorization.Id} but did not return a transfer id. " +
                    "Check the Finix dashboard before retrying fulfilment.");
            }

            var transfer = await WaitForTransferSettlementAsync(captured.Transfer, cancellationToken);
            if (IsFailedTransferState(transfer.State))
            {
                order.UpdateTransferState(transfer.State);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(409,
                    $"Finix capture transfer {transfer.Id} for order {order.Id} ended in {transfer.State}: " +
                    $"{transfer.FailureCode} {transfer.FailureMessage}. Re-authorize the order before fulfilment.");
            }

            var feeCents = await GetFinixFeeCentsAsync(transfer.Id, cancellationToken);
            order.MarkCaptured(
                transfer.Id,
                transfer.State,
                transfer.Amount,
                feeCents,
                transfer.Amount - feeCents);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Captured order {OrderId} with Finix transfer {TransferId}", order.Id, transfer.Id);

            return ToFulfilmentResult(order, false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CancelResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.PaymentStatus == PaymentStatus.Canceled)
            {
                return new CancelResult(
                    order.Id,
                    order.PaymentStatus.ToString(),
                    order.FinixAuthorizationId,
                    order.FinixAuthorizationState,
                    order.LastPaymentError,
                    true);
            }

            if (order.PaymentStatus is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has already been fulfilled and cannot be canceled. Use the refunds endpoint instead.");
            }

            if (order.PaymentStatus == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                FinixAuthorizationResponse voided;
                try
                {
                    voided = await _finixClient.VoidAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    throw new PaymentOperationException(409,
                        $"Finix could not release authorization {order.FinixAuthorizationId} for order {order.Id}: {ex.Message}. " +
                        "Retry the cancellation before fulfilment.");
                }

                order.MarkCanceled(
                    voided.State,
                    voided.VoidState,
                    "Authorization voided; held funds were released.");
                await _orderRepository.UpdateAsync(order, cancellationToken);
                _logger.LogInformation("Canceled order {OrderId} and voided authorization {AuthorizationId}", order.Id, order.FinixAuthorizationId);

                return new CancelResult(
                    order.Id,
                    order.PaymentStatus.ToString(),
                    order.FinixAuthorizationId,
                    order.FinixAuthorizationState,
                    order.LastPaymentError,
                    false);
            }

            order.MarkCanceled(null, null, "Order canceled before any funds were authorized.");
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Canceled unpaid order {OrderId}", order.Id);

            return new CancelResult(
                order.Id,
                order.PaymentStatus.ToString(),
                order.FinixAuthorizationId,
                order.FinixAuthorizationState,
                order.LastPaymentError,
                false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResult> RefundOrderAsync(string buyerId, int orderId, RefundOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentOperationException(400, "An idempotencyKey is required for refunds.");
        }

        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var existingRefund = order.Refunds.FirstOrDefault(refund =>
                string.Equals(refund.IdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal));

            if (existingRefund is not null)
            {
                return ToRefundResult(order, existingRefund, true);
            }

            if (order.PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} cannot be refunded while its payment is {order.PaymentStatus}. " +
                    "Only a captured payment can be refunded.");
            }

            if (string.IsNullOrWhiteSpace(order.FinixTransferId))
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has no captured Finix transfer id to refund.");
            }

            var remainingCents = order.RefundableAmountCents();
            if (remainingCents <= 0)
            {
                throw new PaymentOperationException(409, $"Order {order.Id} has already been fully refunded.");
            }

            var refundCents = command.Amount.HasValue ? ToCents(command.Amount.Value) : remainingCents;
            if (refundCents <= 0)
            {
                throw new PaymentOperationException(400, "The refund amount must be greater than zero.");
            }

            if (refundCents > remainingCents)
            {
                throw new PaymentOperationException(409,
                    $"The requested refund of {FromCents(refundCents):F2} exceeds the remaining refundable amount of {FromCents(remainingCents):F2} {order.Currency}.");
            }

            var capturedCents = order.CapturedAmountCents ?? 0;
            if (refundCents < capturedCents)
            {
                var captureTransfer = await WaitForTransferSettlementAsync(order.FinixTransferId, cancellationToken);
                if (!string.Equals(captureTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    order.UpdateTransferState(captureTransfer.State);
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    throw new PaymentOperationException(409,
                        $"The capture transfer {captureTransfer.Id} for order {order.Id} is {captureTransfer.State}. " +
                        "Finix allows partial refunds only after the capture has succeeded; retry the partial refund after settlement or refund the full amount.");
                }

                order.UpdateTransferState(captureTransfer.State);
            }

            FinixTransferResponse reversal;
            try
            {
                reversal = await _finixClient.CreateReversalAsync(order.FinixTransferId, refundCents, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(409, $"Finix could not refund order {order.Id}: {ex.Message}");
            }

            var refund = new OrderRefund(
                order.Id,
                refundCents,
                command.IdempotencyKey,
                reversal.Id,
                reversal.State);

            order.AddRefund(refund);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Refunded {Amount} {Currency} for order {OrderId} with Finix reversal {ReversalId}",
                FromCents(refundCents), order.Currency, order.Id, reversal.Id);

            return ToRefundResult(order, refund, false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new OrdersByBuyerSpecification(buyerId), cancellationToken);
        return orders.Select(ToOrderSummary).ToList();
    }

    public async Task<SavedCardResult> SaveCardAsync(string buyerId, SaveCardCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Card is null)
        {
            throw new PaymentOperationException(400, "Card details are required.");
        }

        var gate = GetBuyerLock(buyerId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var identityId = await GetOrCreateIdentityAsync(buyerId, command.Card, cancellationToken);
            var instrument = await CreatePaymentInstrumentAsync(buyerId, identityId, command.Card, cancellationToken);

            var card = new SavedCard(
                buyerId,
                instrument.Id,
                instrument.Brand ?? "UNKNOWN",
                instrument.LastFour ?? LastFour(command.Card.Number),
                instrument.ExpirationMonth == 0 ? command.Card.ExpirationMonth : instrument.ExpirationMonth,
                instrument.ExpirationYear == 0 ? command.Card.ExpirationYear : instrument.ExpirationYear,
                instrument.Name ?? command.Card.Name,
                identityId);

            card = await _savedCardRepository.AddAsync(card, cancellationToken);
            _logger.LogInformation("Saved card {PaymentMethodId} for buyer {BuyerId}", card.Id, buyerId);

            return ToSavedCardResult(card);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SavedCardResult>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
        return cards.Select(ToSavedCardResult).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var card = await _savedCardRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (card is null || !string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, "Saved card was not found.");
        }

        if (card.IsDeleted)
        {
            return;
        }

        try
        {
            await _finixClient.DisablePaymentInstrumentAsync(card.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Finix could not disable payment instrument {PaymentInstrumentId}: {Message}", card.FinixPaymentInstrumentId, ex.Message);
        }

        card.MarkDeleted();
        await _savedCardRepository.UpdateAsync(card, cancellationToken);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} for buyer {BuyerId}", card.Id, buyerId);
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "The from date must be earlier than or equal to the to date.");
        }

        var orders = await _orderRepository.ListAsync(new OrdersForReconciliationSpecification(from, to), cancellationToken);
        var authorizations = await _finixClient.ListAuthorizationsAsync(from, to, cancellationToken);
        var transfers = await _finixClient.ListTransfersAsync(from, to, cancellationToken);

        var transactions = new List<ReconciliationFinixTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var authorization in authorizations)
        {
            var orderId = FindOrderForAuthorization(orders, authorization);
            if (orderId.HasValue)
            {
                matchedOrderIds.Add(orderId.Value);
            }

            transactions.Add(new ReconciliationFinixTransaction(
                "AUTHORIZATION",
                authorization.Id,
                authorization.State,
                authorization.Amount,
                FromCents(authorization.Amount),
                authorization.Currency ?? string.Empty,
                authorization.CreatedAt,
                orderId,
                null,
                orderId.HasValue));
        }

        foreach (var transfer in transfers)
        {
            var orderId = FindOrderForTransfer(orders, transfer);
            if (orderId.HasValue)
            {
                matchedOrderIds.Add(orderId.Value);
            }

            transactions.Add(new ReconciliationFinixTransaction(
                "TRANSFER",
                transfer.Id,
                transfer.State,
                transfer.Amount,
                FromCents(transfer.Amount),
                transfer.Currency ?? string.Empty,
                transfer.CreatedAt,
                orderId,
                transfer.ParentTransfer,
                orderId.HasValue));
        }

        var eshopOrders = orders.Select(order =>
        {
            var refundIds = order.Refunds
                .Select(refund => refund.FinixRefundTransferId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            return new ReconciliationOrder(
                order.Id,
                order.BuyerId,
                order.OrderDate,
                order.Total(),
                order.Currency,
                order.PaymentStatus.ToString(),
                order.FinixAuthorizationId,
                order.FinixTransferId,
                refundIds,
                matchedOrderIds.Contains(order.Id));
        }).ToList();

        return new ReconciliationResult(
            from,
            to,
            transactions,
            eshopOrders,
            transactions.Where(transaction => !transaction.Matched).ToList(),
            eshopOrders.Where(order => !order.Matched).ToList(),
            transactions.Count,
            eshopOrders.Count);
    }

    private async Task<decimal> GetFinixFeeCentsAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            var fees = await _finixClient.ListFeesAsync(transferId, cancellationToken);
            if (fees.Count == 0)
            {
                return 0;
            }

            return fees.Sum(fee => fee.Amount);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Could not read Finix fees for transfer {TransferId}: {Message}", transferId, ex.Message);
            return 0;
        }
    }

    private async Task<FinixTransferResponse> WaitForTransferSettlementAsync(string transferId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        var transfer = await _finixClient.GetTransferAsync(transferId, cancellationToken);

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(transfer.State, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            transfer = await _finixClient.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static bool IsFailedTransferState(string? state)
    {
        return string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "RETURNED", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(404, "Order was not found.");
        }

        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, "Order was not found.");
        }

        return order;
    }

    private async Task<ResolvedPaymentInstrument> ResolvePaymentInstrumentAsync(string buyerId, PayOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.PaymentMethodId.HasValue && command.Card is not null)
        {
            throw new PaymentOperationException(400, "Provide either card details or a saved payment method id, not both.");
        }

        if (command.PaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(command.PaymentMethodId.Value, cancellationToken);
            if (savedCard is null || savedCard.IsDeleted || !string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentOperationException(404, "Saved card was not found.");
            }

            return new ResolvedPaymentInstrument(savedCard.FinixPaymentInstrumentId, savedCard.Id);
        }

        if (command.Card is null)
        {
            throw new PaymentOperationException(400, "Provide card details or a saved payment method id.");
        }

        var gate = GetBuyerLock(buyerId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var identityId = await GetOrCreateIdentityAsync(buyerId, command.Card, cancellationToken);
            var instrument = await CreatePaymentInstrumentAsync(buyerId, identityId, command.Card, cancellationToken);
            return new ResolvedPaymentInstrument(instrument.Id, null);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string> GetOrCreateIdentityAsync(string buyerId, CardInput card, CancellationToken cancellationToken)
    {
        var existing = await _finixCustomerRepository.FirstOrDefaultAsync(new FinixCustomerByBuyerSpecification(buyerId), cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var (firstName, lastName) = SplitName(card.Name);
        var request = new FinixCreateIdentityRequest
        {
            Entity = new FinixIdentityEntity
            {
                Email = buyerId.Contains('@') ? buyerId : null,
                FirstName = firstName,
                LastName = lastName,
                PersonalAddress = ToFinixAddress(card.BillingAddress)
            },
            IdentityRoles = new List<string> { "BUYER" },
            Type = "PERSONAL",
            Tags = new Dictionary<string, string> { ["eshop_buyer_id"] = buyerId }
        };

        var identity = await _finixClient.CreateIdentityAsync(request, cancellationToken);
        var customer = new FinixCustomer(buyerId, identity.Id);
        await _finixCustomerRepository.AddAsync(customer, cancellationToken);
        return identity.Id;
    }

    private async Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(string buyerId, string identityId, CardInput card, CancellationToken cancellationToken)
    {
        var number = NormalizeCardNumber(card.Number);
        ValidateExpiration(card.ExpirationMonth, card.ExpirationYear);
        var securityCode = NormalizeSecurityCode(card.SecurityCode);
        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "The cardholder name is required.");
        }

        var request = new FinixCreatePaymentInstrumentRequest
        {
            Address = ToFinixAddress(card.BillingAddress),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name.Trim(),
            Number = number,
            SecurityCode = securityCode,
            Type = "PAYMENT_CARD",
            Tags = new Dictionary<string, string> { ["eshop_buyer_id"] = buyerId }
        };

        try
        {
            return await _finixClient.CreatePaymentInstrumentAsync(request, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(400, $"The card could not be saved with Finix: {ex.Message}");
        }
    }

    private static Address ToAddress(ShippingAddressInput? input)
    {
        return new Address(
            ValueOr(input?.Street, "Not provided"),
            ValueOr(input?.City, "Not provided"),
            input?.State?.Trim() ?? string.Empty,
            ValueOr(input?.Country, "USA"),
            ValueOr(input?.ZipCode, "00000"));
    }

    private static FinixAddress? ToFinixAddress(ShippingAddressInput? input)
    {
        if (input is null)
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = input.Street,
            City = input.City,
            Region = input.State,
            PostalCode = input.ZipCode,
            Country = input.Country
        };
    }

    private static string ValueOr(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizeCardNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new PaymentOperationException(400, "The card number is required.");
        }

        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentOperationException(400, "The card number is not valid.");
        }

        return digits;
    }

    private static string NormalizeSecurityCode(string? securityCode)
    {
        if (string.IsNullOrWhiteSpace(securityCode))
        {
            throw new PaymentOperationException(400, "The card security code is required.");
        }

        var digits = new string(securityCode.Where(char.IsDigit).ToArray());
        if (digits.Length is < 3 or > 4)
        {
            throw new PaymentOperationException(400, "The card security code is not valid.");
        }

        return digits;
    }

    private static void ValidateExpiration(int month, int year)
    {
        if (month is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "The card expiration month must be between 1 and 12.");
        }

        if (year < 2000 || year > 2100)
        {
            throw new PaymentOperationException(400, "The card expiration year is not valid.");
        }
    }

    private static string LastFour(string? cardNumber)
    {
        var digits = new string((cardNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : string.Empty;
    }

    private static (string FirstName, string LastName) SplitName(string? name)
    {
        var parts = (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("eShop", "Shopper");
        }

        if (parts.Length == 1)
        {
            return (parts[0], "Shopper");
        }

        return (parts[0], string.Join(' ', parts.Skip(1)));
    }

    private static long ToCents(decimal amount)
    {
        if (amount <= 0)
        {
            throw new PaymentOperationException(400, "The amount must be greater than zero.");
        }

        var cents = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents > long.MaxValue)
        {
            throw new PaymentOperationException(400, "The amount is too large.");
        }

        return (long)cents;
    }

    private static decimal FromCents(long cents)
    {
        return cents / 100m;
    }

    private static decimal FromCents(decimal cents)
    {
        return cents / 100m;
    }

    private static FulfilmentResult ToFulfilmentResult(Order order, bool replay)
    {
        return new FulfilmentResult(
            order.Id,
            order.PaymentStatus.ToString(),
            order.FinixTransferId,
            order.FinixTransferState,
            order.CapturedAmountCents.HasValue ? FromCents(order.CapturedAmountCents.Value) : 0m,
            order.FinixFeeCents.HasValue ? FromCents(order.FinixFeeCents.Value) : 0m,
            order.NetProceedsCents.HasValue ? FromCents(order.NetProceedsCents.Value) : 0m,
            order.Currency,
            replay);
    }

    private static RefundResult ToRefundResult(Order order, OrderRefund refund, bool replay)
    {
        return new RefundResult(
            refund.Id,
            order.Id,
            refund.FinixRefundTransferId,
            refund.FinixRefundState,
            FromCents(refund.AmountCents),
            FromCents(order.RefundedAmountCents),
            FromCents(order.RefundableAmountCents()),
            order.PaymentStatus.ToString(),
            order.Currency,
            replay);
    }

    private static SavedCardResult ToSavedCardResult(SavedCard card)
    {
        return new SavedCardResult(
            card.Id,
            card.Brand ?? "UNKNOWN",
            card.Last4 ?? string.Empty,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.CardholderName,
            card.FinixPaymentInstrumentId);
    }

    private static OrderSummary ToOrderSummary(Order order)
    {
        return new OrderSummary(
            order.Id,
            order.OrderDate,
            order.Total(),
            order.Currency,
            order.PaymentStatus.ToString(),
            order.FinixAuthorizationId,
            order.FinixAuthorizationState,
            order.FinixTransferId,
            order.FinixTransferState,
            order.CapturedAmountCents.HasValue ? FromCents(order.CapturedAmountCents.Value) : null,
            order.FinixFeeCents.HasValue ? FromCents(order.FinixFeeCents.Value) : null,
            order.NetProceedsCents.HasValue ? FromCents(order.NetProceedsCents.Value) : null,
            FromCents(order.RefundedAmountCents),
            FromCents(order.RefundableAmountCents()),
            order.OrderItems.Select(item => new OrderItemSummary(
                item.ItemOrdered.CatalogItemId,
                item.ItemOrdered.ProductName,
                item.UnitPrice,
                item.Units)).ToList(),
            order.Refunds.Select(refund => new OrderRefundSummary(
                refund.Id,
                FromCents(refund.AmountCents),
                refund.FinixRefundTransferId,
                refund.FinixRefundState,
                refund.CreatedAt)).ToList());
    }

    private static int? FindOrderForAuthorization(IReadOnlyList<Order> orders, FinixAuthorizationResponse authorization)
    {
        var direct = orders.FirstOrDefault(order =>
            string.Equals(order.FinixAuthorizationId, authorization.Id, StringComparison.Ordinal));
        if (direct is not null)
        {
            return direct.Id;
        }

        return FindOrderByTag(orders, authorization.Tags);
    }

    private static int? FindOrderForTransfer(IReadOnlyList<Order> orders, FinixTransferResponse transfer)
    {
        var direct = orders.FirstOrDefault(order =>
            string.Equals(order.FinixTransferId, transfer.Id, StringComparison.Ordinal));
        if (direct is not null)
        {
            return direct.Id;
        }

        var refund = orders.FirstOrDefault(order =>
            order.Refunds.Any(item => string.Equals(item.FinixRefundTransferId, transfer.Id, StringComparison.Ordinal)));
        if (refund is not null)
        {
            return refund.Id;
        }

        if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer))
        {
            var parent = orders.FirstOrDefault(order =>
                string.Equals(order.FinixTransferId, transfer.ParentTransfer, StringComparison.Ordinal));
            if (parent is not null)
            {
                return parent.Id;
            }
        }

        return FindOrderByTag(orders, transfer.Tags);
    }

    private static int? FindOrderByTag(IReadOnlyList<Order> orders, Dictionary<string, string>? tags)
    {
        if (tags is null || !tags.TryGetValue("eshop_payment_reference", out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var order = orders.FirstOrDefault(item =>
            string.Equals(item.PaymentReference, value, StringComparison.Ordinal));
        return order?.Id;
    }

    private static SemaphoreSlim GetOrderLock(int orderId)
    {
        return OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
    }

    private static SemaphoreSlim GetBuyerLock(string buyerId)
    {
        return BuyerLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
    }

    private sealed record ResolvedPaymentInstrument(string PaymentInstrumentId, int? SavedCardId);
}
