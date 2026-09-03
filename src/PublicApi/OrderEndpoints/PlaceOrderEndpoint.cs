using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IShopperOrderService orders, HttpContext http) =>
            {
                return await HandleAsync(request, orders, http);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, IShopperOrderService orders, HttpContext http)
    {
        var shipTo = request.ShipTo ?? new PlaceOrderAddressRequest();
        var order = await orders.PlaceOrderAsync(
            http.User.RequireBuyerId(),
            request.Items.Select(i => new PlaceOrderLine(i.CatalogItemId, i.Quantity)).ToList(),
            new PlaceOrderAddress(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode),
            http.RequestAborted);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
