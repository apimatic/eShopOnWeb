using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the authenticated shopper directly from catalog items, reusing the app's
/// existing order/order-item model. The caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, IOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService) =>
                await HandleAsync(request, user, orderService))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every item quantity must be greater than zero.");
        }

        var lines = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var shippingAddress = ToAddress(request.ShipToAddress);

        var order = await orderService.CreateOrderAsync(buyerId, lines, shippingAddress);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Total = order.Total(),
            Items = order.OrderItems.Select(oi => new CreateOrderItemDto
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(OrderAddressRequest? address)
    {
        if (address is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        }

        return new Address(
            string.IsNullOrWhiteSpace(address.Street) ? "N/A" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "N/A" : address.City,
            string.IsNullOrWhiteSpace(address.State) ? "N/A" : address.State,
            string.IsNullOrWhiteSpace(address.Country) ? "N/A" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "N/A" : address.ZipCode);
    }
}
