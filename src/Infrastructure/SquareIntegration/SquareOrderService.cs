using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Data;
using Microsoft.Extensions.Logging;
using Square.Requests.Orders;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);

public sealed record ShippingAddress(string? Street, string? City, string? State, string? Country, string? ZipCode);

public sealed record PlacedOrder(int OrderId, string? SquareOrderId, string SquareSyncStatus, string? Notice);

public sealed record MyOrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public sealed record MyOrderView(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<MyOrderItemView> Items,
    string? SquareOrderId,
    string? SquareOrderState,
    string SquareSyncStatus,
    string? GiftMessage,
    string? SquareError);

/// <summary>
/// Places shopper orders using eShop's own Order/OrderItem model and mirrors each one to Square with its
/// gift message; reads a shopper's order back with the gift message as Square holds it now.
/// </summary>
public sealed class SquareOrderService
{
    public const int MaxUnitsPerLine = 999;
    public const int MaxLines = 100;

    private readonly CatalogContext _db;
    private readonly IUriComposer _uriComposer;
    private readonly SquareMerchantContextProvider _merchantContext;
    private readonly SquareGiftMessageField _giftMessageField;
    private readonly SquareCatalogLocator _locator;
    private readonly SquareOrderPublisher _publisher;
    private readonly SquareClientHolder _clients;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareOrderService> _logger;

    public SquareOrderService(
        CatalogContext db,
        IUriComposer uriComposer,
        SquareMerchantContextProvider merchantContext,
        SquareGiftMessageField giftMessageField,
        SquareCatalogLocator locator,
        SquareOrderPublisher publisher,
        SquareClientHolder clients,
        TimeProvider clock,
        ILogger<SquareOrderService> logger)
    {
        _db = db;
        _uriComposer = uriComposer;
        _merchantContext = merchantContext;
        _giftMessageField = giftMessageField;
        _locator = locator;
        _publisher = publisher;
        _clients = clients;
        _clock = clock;
        _logger = logger;
    }

    public static string? NormalizeGiftMessage(string? giftMessage)
    {
        var trimmed = giftMessage?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public async Task<PlacedOrder> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        string? giftMessage,
        ShippingAddress? shipTo,
        CancellationToken cancellationToken)
    {
        giftMessage = NormalizeGiftMessage(giftMessage);
        Validate(lines, giftMessage);

        var quantities = lines.GroupBy(l => l.CatalogItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (quantities.Values.Any(q => q > MaxUnitsPerLine))
        {
            throw new SquareRequestException(400, $"At most {MaxUnitsPerLine} units of an item per order.");
        }

        var ids = quantities.Keys.ToArray();
        var catalogItems = await _db.CatalogItems.Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var unknown = ids.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (unknown.Length > 0)
        {
            throw new SquareRequestException(400, $"Unknown catalog item id(s): {string.Join(", ", unknown)}.");
        }

        // Everything Square-side that can fail before anything is written, is checked first.
        var context = await _merchantContext.GetAsync(cancellationToken).ConfigureAwait(false);
        if (giftMessage is not null)
        {
            await _giftMessageField.EnsureDefinitionAsync(context.MerchantId, cancellationToken).ConfigureAwait(false);
        }

        var lookup = await _locator.ResolveAsync(context.MerchantId, ids, cancellationToken).ConfigureAwait(false);

        var orderItems = catalogItems.OrderBy(c => c.Id).Select(c => new OrderItem(
            new CatalogItemOrdered(c.Id, c.Name, _uriComposer.ComposePicUri(c.PictureUri)),
            c.Price,
            quantities[c.Id])).ToList();
        var order = new Order(buyerId, ToAddress(shipTo), orderItems);
        var now = _clock.GetUtcNow();
        var link = new SquareOrderLink
        {
            Order = order,
            MerchantId = context.MerchantId,
            LocationId = context.LocationId,
            IdempotencyKey = Guid.NewGuid().ToString(),
            LineCatalogObjectIds = SquareOrderPublisher.ChooseLineReferences(orderItems, lookup, context.Currency),
            HasGiftMessage = giftMessage is not null,
            Status = SquareOrderSyncStatus.PendingSquareOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // The claim: the eShop order and its link (carrying the Square idempotency key) are saved together
        // before Square is called; every later attempt for this order re-uses this row and key.
        _db.Orders.Add(order);
        _db.SquareOrderLinks.Add(link);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (giftMessage is not null)
        {
            _publisher.HoldGiftMessage(order.Id, giftMessage);
        }

        try
        {
            await _publisher.CreateSquareOrderAsync(_db, order, link, context.Currency, cancellationToken).ConfigureAwait(false);
        }
        catch (SquareIntegrationException ex) when (ex.Kind == SquareFailureKind.OutcomeUnknown)
        {
            _logger.LogWarning("eShop order {OrderId} saved; Square order not confirmed yet — settled on next read", order.Id);
            return new PlacedOrder(order.Id, null, StatusText(link.Status),
                "The order is placed; Square has not confirmed it yet. It is confirmed automatically when the order is read.");
        }
        catch (SquareIntegrationException)
        {
            // Square definitively did not create the order: undo the eShop order so both sides agree.
            _publisher.ForgetGiftMessage(order.Id);
            _db.SquareOrderLinks.Remove(link);
            _db.Orders.Remove(order);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (giftMessage is not null)
        {
            try
            {
                await _publisher.SetGiftMessageAsync(_db, link, giftMessage, settling: false, cancellationToken).ConfigureAwait(false);
            }
            catch (SquareIntegrationException ex)
            {
                _logger.LogWarning("Gift message for eShop order {OrderId} not confirmed by Square ({Kind}) — settled on next read", order.Id, ex.Kind);
                return new PlacedOrder(order.Id, link.SquareOrderId, StatusText(link.Status),
                    "The order is placed in Square; the gift message is not confirmed yet. It is retried when the order is read.");
            }
        }

        return new PlacedOrder(order.Id, link.SquareOrderId, StatusText(link.Status), null);
    }

    /// <summary>The caller's order (null when it does not exist or belongs to someone else).</summary>
    public async Task<MyOrderView?> GetMyOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return null;
        }

        var link = await _db.SquareOrderLinks.SingleOrDefaultAsync(l => l.OrderId == orderId, cancellationToken).ConfigureAwait(false);
        string? squareState = null;
        string? giftMessage = null;
        string? squareError = null;

        if (link is not null)
        {
            try
            {
                if (link.Status != SquareOrderSyncStatus.Synced)
                {
                    await SettleAsync(order, link, cancellationToken).ConfigureAwait(false);
                }

                if (link.SquareOrderId is { } squareOrderId)
                {
                    var squareOrder = await SquareCall.RunAsync("Orders.RetrieveOrder", ct => _clients.Client.Orders.RetrieveOrder(
                        new RetrieveOrderRequest { OrderId = squareOrderId }, cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);
                    squareState = squareOrder.Order?.State?.Value;
                    giftMessage = await _giftMessageField.GetAsync(squareOrderId, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (SquareIntegrationException ex)
            {
                // The eShop order is still returned; the caller is told the Square part is missing.
                squareError = ex.Message;
            }
        }

        return new MyOrderView(
            order.Id,
            order.OrderDate,
            order.Total(),
            order.OrderItems.OrderBy(i => i.Id)
                .Select(i => new MyOrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
            link?.SquareOrderId,
            squareState,
            link is null ? "not_in_square" : StatusText(link.Status),
            giftMessage,
            squareError);
    }

    /// <summary>Finishes Square writes left unconfirmed by an earlier request (same idempotency keys).</summary>
    private async Task SettleAsync(Order order, SquareOrderLink link, CancellationToken cancellationToken)
    {
        var context = await _merchantContext.GetAsync(cancellationToken).ConfigureAwait(false);
        if (context.MerchantId != link.MerchantId)
        {
            throw new SquareIntegrationException(SquareFailureKind.NotConnected,
                "This order belongs to a Square merchant the shop is no longer connected to.");
        }

        if (link.Status == SquareOrderSyncStatus.PendingSquareOrder)
        {
            await _publisher.CreateSquareOrderAsync(_db, order, link, context.Currency, cancellationToken).ConfigureAwait(false);
        }

        if (link.Status == SquareOrderSyncStatus.PendingGiftMessage)
        {
            // May be null after a restart; then only a value Square already holds can settle it.
            var giftMessage = _publisher.HeldGiftMessage(order.Id);
            await _giftMessageField.EnsureDefinitionAsync(context.MerchantId, cancellationToken).ConfigureAwait(false);
            await _publisher.SetGiftMessageAsync(_db, link, giftMessage, settling: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Validate(IReadOnlyCollection<OrderLineRequest> lines, string? giftMessage)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new SquareRequestException(400, "An order needs at least one item.");
        }

        if (lines.Count > MaxLines)
        {
            throw new SquareRequestException(400, $"An order can have at most {MaxLines} lines.");
        }

        if (lines.Any(l => l.CatalogItemId <= 0))
        {
            throw new SquareRequestException(400, "Catalog item ids must be positive.");
        }

        if (lines.Any(l => l.Quantity < 1 || l.Quantity > MaxUnitsPerLine))
        {
            throw new SquareRequestException(400, $"Quantities must be between 1 and {MaxUnitsPerLine}.");
        }

        if (giftMessage is not null && giftMessage.Length > SquareConstants.MaxGiftMessageLength)
        {
            throw new SquareRequestException(400, $"The gift message can be at most {SquareConstants.MaxGiftMessageLength} characters.");
        }
    }

    /// <summary>The existing model requires an address; an order without one (collected in person) stores empty fields.</summary>
    private static Address ToAddress(ShippingAddress? shipTo) => new(
        shipTo?.Street?.Trim() ?? string.Empty,
        shipTo?.City?.Trim() ?? string.Empty,
        shipTo?.State?.Trim() ?? string.Empty,
        shipTo?.Country?.Trim() ?? string.Empty,
        shipTo?.ZipCode?.Trim() ?? string.Empty);

    private static string StatusText(SquareOrderSyncStatus status) => status switch
    {
        SquareOrderSyncStatus.Synced => "synced",
        SquareOrderSyncStatus.PendingGiftMessage => "pending_gift_message",
        _ => "pending_square_order",
    };
}
