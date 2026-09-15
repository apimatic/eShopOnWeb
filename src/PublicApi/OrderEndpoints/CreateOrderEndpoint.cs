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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext http, IShopOrderService service) =>
            {
                return await HandleAsync(request, http, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IShopOrderService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http, IShopOrderService service)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);
        var items = request.Items.Select(i => new CatalogOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var order = await service.PlaceOrderAsync(buyerId, items);
        var response = OrderMapper.ToCreateResponse(order, request.CorrelationId());
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
