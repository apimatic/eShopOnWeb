using System.Linq;
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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                return await HandleAsync(request, shopperOrderService, httpContext);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService shopperOrderService)
        => HandleAsync(request, shopperOrderService, httpContext: null!);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService shopperOrderService, HttpContext httpContext)
    {
        var buyerId = ShopperIdentity.TryGetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var shipTo = request.ShipTo is null
            ? new Address("123 Main Street", "Seattle", "WA", "USA", "98101")
            : new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);

        var order = await shopperOrderService.PlaceOrderAsync(new Microsoft.eShopWeb.ApplicationCore.Interfaces.PlaceOrderRequest
        {
            BuyerId = buyerId,
            Items = request.Items.Select(i => new PlaceOrderItem
            {
                CatalogItemId = i.CatalogItemId,
                Quantity = i.Quantity
            }).ToList(),
            ShipTo = shipTo
        });

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };

        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
