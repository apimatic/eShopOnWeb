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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPublicApiOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IPublicApiOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, orderService, httpContext);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IPublicApiOrderService orderService)
        => HandleAsync(request, orderService, null!);

    private async Task<IResult> HandleAsync(PlaceOrderRequest request, IPublicApiOrderService orderService, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        var lines = (request.Items ?? new System.Collections.Generic.List<PlaceOrderItemRequest>()).Select(i => new CatalogOrderLine
        {
            CatalogItemId = i.CatalogItemId,
            Quantity = i.Quantity
        }).ToList();

        var order = await orderService.PlaceOrderAsync(buyerId, lines);
        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
