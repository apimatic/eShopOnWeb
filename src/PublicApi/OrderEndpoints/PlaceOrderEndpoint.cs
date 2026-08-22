using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationOrchestrator>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, IOrderNotificationOrchestrator orchestrator, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, orchestrator, user);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationOrchestrator orchestrator)
    {
        return HandleAsync(request, orchestrator, new ClaimsPrincipal());
    }

    private async Task<IResult> HandleAsync(
        PlaceOrderRequest request,
        IOrderNotificationOrchestrator orchestrator,
        ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        PlaceOrderAddress? address = request.ShipTo is null
            ? null
            : new PlaceOrderAddress(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);

        var items = request.Items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var result = await orchestrator.PlaceOrderAsync(buyerId, items, address);
        return result.ToHttpResult(order =>
        {
            var response = new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        });
    }
}
