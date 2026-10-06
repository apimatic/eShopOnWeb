using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);

public sealed record PlaceOrderAddress(string Street, string City, string? State, string Country, string ZipCode);

public sealed record PlaceOrderCommand(IReadOnlyList<PlaceOrderLine> Items, string? GiftMessage, PlaceOrderAddress? ShipToAddress);

public static class SquareOrderStatus
{
    /// <summary>The Square order exists.</summary>
    public const string Created = "Created";
    /// <summary>Square has not confirmed the order yet; reading the order retries with the same idempotency key.</summary>
    public const string Pending = "Pending";
    /// <summary>The order was placed elsewhere (e.g. the storefront) and was never sent to Square.</summary>
    public const string NotSynced = "NotSynced";
}

public sealed record PlacedOrder(int OrderId, string? SquareOrderId, string SquareStatus, bool GiftMessageSaved, string? Warning);

public sealed record MyOrder(Order Order, string? SquareOrderId, string SquareStatus, string? SquareOrderState,
    string? GiftMessage, bool GiftMessageAvailable, string? Warning);

/// <summary>A request the caller can fix (unknown catalog item, bad quantity...).</summary>
public sealed class OrderRequestException : Exception
{
    public OrderRequestException(string message) : base(message) { }
}

/// <summary>
/// Places shopper orders using eShop's own Order/OrderItem model and mirrors each one to Square, with the gift
/// message kept only on the Square order.
/// </summary>
public sealed class SquareOrderService
{
    private readonly CatalogContext _db;
    private readonly SquareMerchantContextProvider _merchants;
    private readonly SquareOrderSync _orderSync;
    private readonly SquareGiftMessageStore _giftMessages;
    private readonly IUriComposer _uriComposer;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareOrderService> _logger;

    public SquareOrderService(CatalogContext db, SquareMerchantContextProvider merchants, SquareOrderSync orderSync,
        SquareGiftMessageStore giftMessages, IUriComposer uriComposer, TimeProvider clock, ILogger<SquareOrderService> logger)
    {
        _db = db;
        _merchants = merchants;
        _orderSync = orderSync;
        _giftMessages = giftMessages;
        _uriComposer = uriComposer;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PlacedOrder> PlaceOrderAsync(string buyerId, PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var quantities = command.Items
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var ids = quantities.Keys.ToList();
        var catalogItems = await _db.CatalogItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        var missing = ids.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
            throw new OrderRequestException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        // Resolve Square before anything is stored: if Square cannot take the order, nothing is placed.
        var merchant = await _merchants.GetAsync(cancellationToken);
        var giftMessage = string.IsNullOrWhiteSpace(command.GiftMessage) ? null : command.GiftMessage.Trim();
        if (giftMessage is not null)
            await _giftMessages.EnsureDefinitionAsync(merchant.MerchantId, cancellationToken);

        var orderItems = catalogItems.Select(item =>
        {
            SquareMoney.ToMinorUnits(item.Price, merchant.Currency.Value); // refuses a price Square cannot represent
            var pictureUri = _uriComposer.ComposePicUri(string.IsNullOrEmpty(item.PictureUri) ? "eCatalog-item-default.png" : item.PictureUri);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, pictureUri), item.Price, quantities[item.Id]);
        }).ToList();

        var now = _clock.GetUtcNow();
        var order = new Order(buyerId, ToAddress(command.ShipToAddress, merchant), orderItems);
        var link = new SquareOrderLink
        {
            Order = order,
            MerchantId = merchant.MerchantId,
            LocationId = merchant.LocationId,
            Currency = merchant.Currency.Value,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            State = SquareOrderLinkState.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // The eShop order and its Square claim are saved together, before Square is called.
        _db.Orders.Add(order);
        _db.SquareOrderLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (SquareClaims.IsDuplicateKey(ex))
        {
            throw new SquareIntegrationException(SquareFailureKind.Conflict, "The order could not be recorded.", innerException: ex);
        }

        string squareOrderId;
        try
        {
            squareOrderId = await _orderSync.CreateSquareOrderAsync(link, order, cancellationToken);
        }
        catch (SquareIntegrationException ex) when (ex.Kind == SquareFailureKind.OutcomeUnknown)
        {
            _logger.LogWarning("Square order for eShop order {OrderId} is pending: Square did not confirm it.", order.Id);
            return new PlacedOrder(order.Id, null, SquareOrderStatus.Pending, false,
                "Square has not confirmed the order yet; it will be completed when the order is read. "
                + (giftMessage is null ? string.Empty : "The gift message was not saved."));
        }
        catch (SquareIntegrationException)
        {
            // Square refused the order: withdraw it so the shopper is not left with an order Square staff never see.
            _db.SquareOrderLinks.Remove(link);
            _db.RemoveRange(order.OrderItems);
            _db.Orders.Remove(order);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        if (giftMessage is null)
            return new PlacedOrder(order.Id, squareOrderId, SquareOrderStatus.Created, false, null);

        try
        {
            await _giftMessages.SetAsync(squareOrderId, giftMessage, "gm-" + link.IdempotencyKey, cancellationToken);
            return new PlacedOrder(order.Id, squareOrderId, SquareOrderStatus.Created, true, null);
        }
        catch (SquareIntegrationException ex)
        {
            _logger.LogWarning("Saving the gift message on Square order {SquareOrderId} failed ({Kind}, {Code}).",
                squareOrderId, ex.Kind, ex.SquareErrorCode);
            return new PlacedOrder(order.Id, squareOrderId, SquareOrderStatus.Created, false,
                "The order was placed but Square did not save the gift message.");
        }
    }

    /// <returns>The caller's order, or null when it does not exist or belongs to someone else.</returns>
    public async Task<MyOrder?> GetMyOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken);
        if (order is null) return null;

        var link = await _db.SquareOrderLinks.SingleOrDefaultAsync(l => l.OrderId == orderId, cancellationToken);
        if (link is null)
            return new MyOrder(order, null, SquareOrderStatus.NotSynced, null, null, false, null);

        if (link.State == SquareOrderLinkState.Pending)
        {
            try
            {
                // Same key, same request: Square returns the order if the earlier attempt created it.
                await _orderSync.CreateSquareOrderAsync(link, order, cancellationToken);
            }
            catch (SquareIntegrationException ex)
            {
                return new MyOrder(order, null, SquareOrderStatus.Pending, null, null, false,
                    $"Square has not confirmed the order yet ({ex.Kind}).");
            }
        }

        try
        {
            var merchant = await _merchants.GetAsync(cancellationToken);
            if (merchant.MerchantId != link.MerchantId)
                return new MyOrder(order, link.SquareOrderId, SquareOrderStatus.Created, null, null, false,
                    "The order is in a Square account other than the one connected now.");

            var squareOrder = await _orderSync.RetrieveAsync(link.SquareOrderId!, cancellationToken);
            var giftMessage = await _giftMessages.GetAsync(link.SquareOrderId!, cancellationToken);
            return new MyOrder(order, link.SquareOrderId, SquareOrderStatus.Created, squareOrder?.State?.Value, giftMessage, true, null);
        }
        catch (SquareIntegrationException ex)
        {
            return new MyOrder(order, link.SquareOrderId, SquareOrderStatus.Created, null, null, false,
                $"Square could not be read right now ({ex.Kind}); the gift message is unavailable.");
        }
    }

    private static Address ToAddress(PlaceOrderAddress? requested, SquareMerchantContext merchant)
    {
        if (requested is not null)
            return new Address(requested.Street, requested.City, requested.State ?? string.Empty, requested.Country, requested.ZipCode);

        // No shipping address: the order is collected at the Square location.
        var location = merchant.LocationAddress;
        return new Address(
            Truncate(location?.AddressLine1 ?? $"In-store pickup at {merchant.LocationName ?? "the store"}", 180),
            Truncate(location?.Locality ?? merchant.LocationName ?? "Store pickup", 100),
            Truncate(location?.AdministrativeDistrictLevel1 ?? string.Empty, 60),
            Truncate(location?.Country?.Value ?? "N/A", 90),
            Truncate(location?.PostalCode ?? "N/A", 18));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
