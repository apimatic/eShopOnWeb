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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
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

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkoutService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = HttpCaller.RequireUserName(httpContext);
        var order = await checkoutService.PlaceOrderAsync(
            new PlaceOrderCommand(
                buyerId,
                request.Items.Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList(),
                request.ShipTo.ToAddress()),
            httpContext.RequestAborted);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.From(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
