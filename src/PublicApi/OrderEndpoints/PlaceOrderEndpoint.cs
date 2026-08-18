using System.Collections.Generic;
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

/// <summary>
/// POST /api/orders — places an order for the signed-in shopper from catalog items, reusing the app's
/// existing Order/OrderItem model. The shopper is told their order was placed. Returns the new orderId.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    // Default shipping address used when the request omits one (mirrors the storefront sample default).
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
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        var buyerId = EndpointCaller.UserName(_httpContextAccessor);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one order item is required." });
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShipToAddress is null
            ? DefaultAddress
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await service.PlaceOrderAsync(buyerId, lines, address, EndpointCaller.RequestAborted(_httpContextAccessor));
        if (order is null)
        {
            return Results.BadRequest(new { error = "One or more catalog items do not exist, or a quantity was not positive." });
        }

        var response = new PlaceOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            Message = "Your order was placed. If you have a mobile number on file, we've texted you a confirmation."
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
