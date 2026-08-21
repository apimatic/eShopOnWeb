using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(request, notificationService);
            })
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService notificationService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = EndpointUser.RequireBuyerId(httpContext.User);
        var items = request.Items.Select(i => new CatalogQuantity(i.CatalogItemId, i.Quantity)).ToList();
        var address = new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = await notificationService.PlaceOrderAsync(buyerId, items, address, httpContext.RequestAborted);
        return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id
        });
    }
}
