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
/// Places an order from catalog items for the authenticated shopper, reusing the app's existing
/// order/order-item model. The caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var items = request.Items
            .Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = MapAddress(request.ShipToAddress);

        var order = await orderService.CreateOrderAsync(request.BuyerId, items, address);

        response.OrderId = order.Id;
        response.Total = order.Total();
        response.OrderDate = order.OrderDate;
        response.Items = order.OrderItems
            .Select(oi => new OrderLineDto
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            })
            .ToList();

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address MapAddress(OrderAddressDto? dto)
    {
        // Shipping address is not part of the bill; a placeholder is used when the caller omits it.
        if (dto is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }

        return new Address(
            NullToPlaceholder(dto.Street),
            NullToPlaceholder(dto.City),
            dto.State ?? string.Empty,
            NullToPlaceholder(dto.Country),
            NullToPlaceholder(dto.ZipCode, "00000"));
    }

    private static string NullToPlaceholder(string? value, string placeholder = "N/A") =>
        string.IsNullOrWhiteSpace(value) ? placeholder : value;
}
