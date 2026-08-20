using System;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, ICheckoutService checkout) =>
            {
                request.BuyerId = Caller.UserName(http);
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        var items = request.Items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkout.PlaceOrderAsync(
            request.BuyerId!,
            new PlaceOrderRequest(items, OrderDtoMapper.ToAddress(request.ShipTo)));
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order, checkout.Currency)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

internal static class Caller
{
    public static string UserName(HttpContext http)
    {
        var name = http.User.Identity?.Name ?? http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UnauthorizedAccessException("A signed-in shopper is required.");
        }

        return name;
    }
}
