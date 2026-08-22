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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ICheckoutPaymentService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutPaymentService service) =>
        HandleAsync(request, service, null!);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutPaymentService service, HttpContext http)
    {
        var buyerId = EndpointIdentity.RequireUserName(http);
        var items = (request.Items ?? []).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(buyerId, items, EndpointIdentity.ToAddress(request.ShipToAddress), http.RequestAborted);
        var response = OrderResponseMapper.From(order);
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
