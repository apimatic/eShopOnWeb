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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaidOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IPaidOrderService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IPaidOrderService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private static async Task<IResult> HandleAsync(CreateOrderRequest request, IPaidOrderService service, ClaimsPrincipal user)
    {
        var lines = (request.Items ?? []).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.CreateOrderAsync(user.GetRequiredUserName(), lines, OrderDtoMapper.ToAddress(request.ShipToAddress));
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
