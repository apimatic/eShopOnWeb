using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Orders;
using EshopOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using EshopOrderItem = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.OrderItem;
using SquareOrder = Square.Models.Order;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>
/// Writes an eShop order to Square: the Square order (same items and prices, at the merchant's location)
/// and its gift message. Every write carries an idempotency key and an unknown outcome is settled by
/// re-sending with the same key.
/// </summary>
public sealed class SquareOrderPublisher
{
    private static readonly TimeSpan PendingGiftMessageLifetime = TimeSpan.FromHours(24);

    private readonly SquareClientHolder _clients;
    private readonly SquareGiftMessageField _giftMessageField;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareOrderPublisher> _logger;

    public SquareOrderPublisher(
        SquareClientHolder clients,
        SquareGiftMessageField giftMessageField,
        IMemoryCache cache,
        TimeProvider clock,
        ILogger<SquareOrderPublisher> logger)
    {
        _clients = clients;
        _giftMessageField = giftMessageField;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Chooses, per order item, the Square variation to reference — only one this shop created for that
    /// item and whose current Square price equals the order's unit price; otherwise an ad-hoc line.
    /// Stored as "catalogItemId=variationId" pairs (empty variation = ad-hoc line).
    /// </summary>
    public static string ChooseLineReferences(IReadOnlyList<EshopOrderItem> items, SquareCatalogLookup lookup, Currency currency) =>
        string.Join(",", items.Select(item =>
        {
            var catalogItemId = item.ItemOrdered.CatalogItemId;
            var variationId = lookup.Found.TryGetValue(catalogItemId, out var entry)
                              && SquareMoney.Matches(entry.Variation.ItemVariationData?.PriceMoney, item.UnitPrice, currency)
                ? entry.Variation.Id
                : string.Empty;
            return $"{catalogItemId.ToString(CultureInfo.InvariantCulture)}={variationId}";
        }));

    private static Dictionary<int, string> ParseLineReferences(string references) =>
        references.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToDictionary(parts => int.Parse(parts[0], CultureInfo.InvariantCulture), parts => parts[1]);

    /// <summary>
    /// The gift message is held in process memory only until Square confirms it — never in the database.
    /// </summary>
    public void HoldGiftMessage(int orderId, string giftMessage) =>
        _cache.Set(GiftCacheKey(orderId), giftMessage, PendingGiftMessageLifetime);

    public string? HeldGiftMessage(int orderId) => _cache.Get<string>(GiftCacheKey(orderId));

    public void ForgetGiftMessage(int orderId) => _cache.Remove(GiftCacheKey(orderId));

    public async Task CreateSquareOrderAsync(CatalogContext db, EshopOrder order, SquareOrderLink link, Currency currency, CancellationToken cancellationToken)
    {
        var request = BuildCreateOrderRequest(order, link, currency);
        CreateOrderResponse response;
        try
        {
            response = await SendCreateOrderAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (SquareIntegrationException ex) when (ex.MayHaveReachedSquare)
        {
            _logger.LogWarning("Square order for eShop order {OrderId}: outcome unknown ({Kind}); re-sending with the same idempotency key", order.Id, ex.Kind);
            try
            {
                response = await SendCreateOrderAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (SquareIntegrationException retry) when (retry.MayHaveReachedSquare)
            {
                throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown,
                    "Square has not confirmed the order yet.", retry.ProviderStatus, retry.ErrorCodes, retry);
            }
        }

        var created = response.Order;
        if (created?.Id is not { Length: > 0 } squareOrderId)
        {
            throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown, "Square's answer did not include the order id.");
        }

        link.SquareOrderId = squareOrderId;
        link.Status = link.HasGiftMessage ? SquareOrderSyncStatus.PendingGiftMessage : SquareOrderSyncStatus.Synced;
        link.UpdatedAt = _clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

        var expected = SquareMoney.ToMinorUnits(order.Total(), currency);
        if (created.TotalMoney?.Amount is { } total && total != expected)
        {
            _logger.LogInformation(
                "Square order {SquareOrderId} total {SquareTotal} differs from eShop order {OrderId} total {EshopTotal} (minor units; taxes or discounts configured in Square apply)",
                squareOrderId, total, order.Id, expected);
        }

        _logger.LogInformation("Created Square order {SquareOrderId} for eShop order {OrderId}", squareOrderId, order.Id);
    }

    /// <param name="settling">
    /// True when finishing an earlier, unconfirmed attempt: the field is read first, and any value Square
    /// already holds (our earlier write, or a staff edit) is kept rather than written over.
    /// </param>
    public async Task SetGiftMessageAsync(CatalogContext db, SquareOrderLink link, string? giftMessage, bool settling, CancellationToken cancellationToken)
    {
        var squareOrderId = link.SquareOrderId ?? throw new InvalidOperationException("The Square order does not exist yet.");
        var alreadySet = settling && await _giftMessageField.GetAsync(squareOrderId, cancellationToken).ConfigureAwait(false) is not null;
        if (!alreadySet)
        {
            if (giftMessage is null)
            {
                throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown,
                    "Square did not confirm this order's gift message and it is no longer held by the shop; staff can enter it in Square.");
            }

            try
            {
                await WriteGiftMessageAsync(squareOrderId, giftMessage, cancellationToken).ConfigureAwait(false);
            }
            catch (SquareIntegrationException ex) when (ex.Kind == SquareFailureKind.Rejected)
            {
                // Square requires the current version to change an existing value, so a rejection can mean
                // an earlier attempt already stored it. Settle by reading.
                if (await _giftMessageField.GetAsync(squareOrderId, cancellationToken).ConfigureAwait(false) is null)
                {
                    throw;
                }
            }
        }

        link.Status = SquareOrderSyncStatus.Synced;
        link.UpdatedAt = _clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        ForgetGiftMessage(link.OrderId);
    }

    private async Task WriteGiftMessageAsync(string squareOrderId, string giftMessage, CancellationToken cancellationToken)
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        try
        {
            await _giftMessageField.SetAsync(squareOrderId, giftMessage, idempotencyKey, cancellationToken).ConfigureAwait(false);
        }
        catch (SquareIntegrationException ex) when (ex.MayHaveReachedSquare)
        {
            _logger.LogWarning("Gift message for Square order {SquareOrderId}: outcome unknown ({Kind}); re-sending with the same idempotency key", squareOrderId, ex.Kind);
            try
            {
                await _giftMessageField.SetAsync(squareOrderId, giftMessage, idempotencyKey, cancellationToken).ConfigureAwait(false);
            }
            catch (SquareIntegrationException retry) when (retry.MayHaveReachedSquare)
            {
                throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown,
                    "Square has not confirmed the gift message yet.", retry.ProviderStatus, retry.ErrorCodes, retry);
            }
        }
    }

    private Task<CreateOrderResponse> SendCreateOrderAsync(CreateOrderOperationRequest request, CancellationToken cancellationToken) =>
        SquareCall.RunAsync("Orders.CreateOrder",
            ct => _clients.Client.Orders.CreateOrder(request, cancellationToken: ct), _logger, cancellationToken);

    /// <summary>Deterministic from the stored order and link, so a later re-send is the identical request.</summary>
    private static CreateOrderOperationRequest BuildCreateOrderRequest(EshopOrder order, SquareOrderLink link, Currency currency)
    {
        var references = ParseLineReferences(link.LineCatalogObjectIds);
        var lines = order.OrderItems
            .OrderBy(i => i.ItemOrdered.CatalogItemId)
            .Select(item =>
            {
                var quantity = item.Units.ToString(CultureInfo.InvariantCulture);
                return references.TryGetValue(item.ItemOrdered.CatalogItemId, out var catalogObjectId) && catalogObjectId.Length > 0
                    ? new OrderLineItem { Quantity = quantity, CatalogObjectId = catalogObjectId, ItemType = OrderLineItemItemType.Item }
                    : new OrderLineItem
                    {
                        Quantity = quantity,
                        Name = item.ItemOrdered.ProductName,
                        BasePriceMoney = SquareMoney.From(item.UnitPrice, currency),
                        ItemType = OrderLineItemItemType.Item,
                    };
            }).ToArray();

        return new CreateOrderOperationRequest
        {
            Body = new CreateOrderRequest
            {
                IdempotencyKey = link.IdempotencyKey,
                Order = new SquareOrder
                {
                    LocationId = link.LocationId,
                    ReferenceId = $"eshop-{order.Id}",
                    LineItems = lines,
                },
            },
        };
    }

    private static string GiftCacheKey(int orderId) => $"square:pending-gift-message:{orderId}";
}
