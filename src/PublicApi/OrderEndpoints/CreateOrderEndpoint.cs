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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
                await HandleAsync(request, service, user))
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
        => HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        var lines = (request.Items ?? []).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.CreateOrderAsync(buyerId, lines, PaymentEndpointHelpers.ToAddress(request.ShipToAddress));
        return Results.Created($"api/orders/{order.Id}", PaymentEndpointHelpers.ToResponse(order));
    }
}
