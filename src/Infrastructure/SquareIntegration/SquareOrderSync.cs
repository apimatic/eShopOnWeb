using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration.Entities;
using Microsoft.Extensions.Logging;
using Square.Core.Exceptions;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Orders;
using EShopOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using SquareOrder = Square.Models.Order;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>Creates the Square order for an eShop order, at the merchant's location, with the same lines and prices.</summary>
public sealed class SquareOrderSync
{
    public const string SourceName = "eShopOnWeb";

    private readonly CatalogContext _db;
    private readonly SquareClientProvider _clients;
    private readonly TimeProvider _clock;
    private readonly ILogger<SquareOrderSync> _logger;

    public SquareOrderSync(CatalogContext db, SquareClientProvider clients, TimeProvider clock, ILogger<SquareOrderSync> logger)
    {
        _db = db;
        _clients = clients;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Sends CreateOrder with the link's stored idempotency key. The request is rebuilt from the stored eShop order,
    /// so a later re-send (to settle an unknown outcome) is identical and Square returns the same order.
    /// </summary>
    public async Task<string> CreateSquareOrderAsync(SquareOrderLink link, EShopOrder order, CancellationToken cancellationToken)
    {
        if (link.SquareOrderId is not null) return link.SquareOrderId;

        var request = new CreateOrderOperationRequest { Body = BuildRequest(link, order) };
        CreateOrderResponse response;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                response = await _clients.Merchant.Orders.CreateOrder(request, cancellationToken: cancellationToken);
                break;
            }
            catch (SdkConnectionException ex) when (attempt == 0)
            {
                _logger.LogWarning(ex, "Square CreateOrder for eShop order {OrderId} has an unknown outcome; re-sending with the same idempotency key.",
                    order.Id);
            }
            catch (SdkException ex)
            {
                var failure = SquareErrors.Translate(ex, "creating the Square order", isWrite: true);
                link.LastErrorCode = failure.SquareErrorCode ?? failure.Kind.ToString();
                link.UpdatedAt = _clock.GetUtcNow();
                await _db.SaveChangesAsync(CancellationToken.None);
                throw failure;
            }
        }

        var squareOrderId = response.Order?.Id
            ?? throw new SquareIntegrationException(SquareFailureKind.OutcomeUnknown, "Square returned no order id.");
        link.SquareOrderId = squareOrderId;
        link.State = SquareOrderLinkState.Created;
        link.LastErrorCode = null;
        link.UpdatedAt = _clock.GetUtcNow();
        link.ConcurrencyStamp = Guid.NewGuid();
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Created Square order {SquareOrderId} for eShop order {OrderId}.", squareOrderId, order.Id);
        return squareOrderId;
    }

    public async Task<SquareOrder?> RetrieveAsync(string squareOrderId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _clients.Merchant.Orders.RetrieveOrder(
                new RetrieveOrderRequest { OrderId = squareOrderId }, cancellationToken: cancellationToken);
            return response.Order;
        }
        catch (SdkException ex)
        {
            throw SquareErrors.Translate(ex, "reading the Square order");
        }
    }

    public static CreateOrderRequest BuildRequest(SquareOrderLink link, EShopOrder order)
    {
        if (!Currency.TryGetKnownValue(link.Currency, out var currency))
            throw new SquareIntegrationException(SquareFailureKind.Rejected, $"Unsupported currency {link.Currency}.");

        var orderId = order.Id.ToString(CultureInfo.InvariantCulture);
        return new CreateOrderRequest
        {
            IdempotencyKey = link.IdempotencyKey,
            Order = new SquareOrder
            {
                LocationId = link.LocationId,
                ReferenceId = orderId,
                Source = new OrderSource { Name = SourceName },
                Metadata = new Dictionary<string, string> { ["eshop_order_id"] = orderId },
                LineItems = order.OrderItems
                    .OrderBy(i => i.Id)
                    .Select(i => new OrderLineItem
                    {
                        Name = i.ItemOrdered.ProductName,
                        Quantity = i.Units.ToString(CultureInfo.InvariantCulture),
                        BasePriceMoney = new Money
                        {
                            Amount = SquareMoney.ToMinorUnits(i.UnitPrice, currency.Value),
                            Currency = currency,
                        },
                        Metadata = new Dictionary<string, string>
                        {
                            ["eshop_catalog_item_id"] = i.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
                        },
                    })
                    .ToList(),
            },
        };
    }
}
