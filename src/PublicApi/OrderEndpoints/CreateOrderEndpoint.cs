using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the authenticated shopper from catalog items and quantities, reusing eShop's
/// existing order/order-item model. The buyer comes from the token; the response returns the new
/// <c>orderId</c> so a bill can be raised against it.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>, IRepository<CatalogItem>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                IRepository<CatalogItem> catalogRepository,
                CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await ExecuteAsync(request, buyerId, orderRepository, catalogRepository, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    // Interface member — the live route path runs ExecuteAsync with the token identity and cancellation.
    public Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository) =>
        ExecuteAsync(request, string.Empty, orderRepository, catalogRepository, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(CreateOrderRequest request, string buyerId,
        IRepository<Order> orderRepository, IRepository<CatalogItem> catalogRepository, CancellationToken ct)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var items = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                return Results.BadRequest($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = await catalogRepository.GetByIdAsync(line.CatalogItemId, ct);
            if (catalogItem is null)
            {
                return Results.BadRequest($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = BuildAddress(request.ShipToAddress);
        var order = new Order(buyerId, address, items);
        order = await orderRepository.AddAsync(order, ct);

        response.OrderId = order.Id;
        response.BuyerId = buyerId;
        response.Total = order.Total();
        response.Items = order.OrderItems.Select(oi => new CreateOrderResponseItem
        {
            CatalogItemId = oi.ItemOrdered.CatalogItemId,
            ProductName = oi.ItemOrdered.ProductName,
            UnitPrice = oi.UnitPrice,
            Units = oi.Units
        }).ToList();

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(CreateOrderAddress? a)
    {
        // The order aggregate requires a ship-to address; supply sensible placeholders when the
        // shopper does not provide one (this API's focus is billing, not fulfilment).
        return new Address(
            string.IsNullOrWhiteSpace(a?.Street) ? "N/A" : a!.Street!,
            string.IsNullOrWhiteSpace(a?.City) ? "N/A" : a!.City!,
            string.IsNullOrWhiteSpace(a?.State) ? "N/A" : a!.State!,
            string.IsNullOrWhiteSpace(a?.Country) ? "N/A" : a!.Country!,
            string.IsNullOrWhiteSpace(a?.ZipCode) ? "00000" : a!.ZipCode!);
    }
}
