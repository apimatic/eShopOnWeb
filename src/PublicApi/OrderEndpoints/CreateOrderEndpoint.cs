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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IShopperOrderService shopperOrderService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, shopperOrderService, user);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService shopperOrderService)
        => HandleAsync(request, shopperOrderService, null);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IShopperOrderService shopperOrderService, ClaimsPrincipal? user)
    {
        var buyerId = BuyerIdentity.GetBuyerId(user ?? new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? []).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await shopperOrderService.PlaceOrderAsync(buyerId, lines, default);
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
