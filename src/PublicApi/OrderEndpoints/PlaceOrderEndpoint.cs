using System.Linq;
using System.Threading;
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

/// <summary>
/// POST /api/orders — place an order from catalog item ids and quantities, reusing the app's existing
/// Order/OrderItem model. The shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    // Mirrors the storefront checkout, which ships to a default address in this sample app.
    private static readonly Address DefaultAddress = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var buyerId = http.User.Identity!.Name!;

        var lines = (request.Items ?? new())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = request.ShippingAddress is null
            ? DefaultAddress
            : new Address(request.ShippingAddress.Street, request.ShippingAddress.City, request.ShippingAddress.State,
                request.ShippingAddress.Country, request.ShippingAddress.ZipCode);

        var orderId = await service.PlaceOrderAsync(buyerId, lines, address, http.RequestAborted);

        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse
        {
            OrderId = orderId,
            Status = OrderStatus.Placed.ToString()
        });
    }
}
