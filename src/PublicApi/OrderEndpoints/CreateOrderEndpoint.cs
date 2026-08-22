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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _http;

    public CreateOrderEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ICheckoutService checkout) =>
            {
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        var buyerId = HttpUser.RequireBuyerId(_http.HttpContext!);
        var items = request.Items.Select(i => new CreatePaidOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var order = await checkout.PlaceOrderAsync(buyerId, items, request.ShipTo?.ToAddress(), _http.HttpContext!.RequestAborted);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
