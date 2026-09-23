using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public class PlaceOrderRequest
{
    public List<PlaceOrderItem> Items { get; set; } = new();
    /// <summary>Set from the caller's token; never bound from the body.</summary>
    public string? BuyerId { get; set; }
}

public record PlaceOrderResponse(int OrderId);

/// <summary>
/// Places an order for the signed-in shopper from catalog items and tells them it was placed. Reuses the
/// existing Order/OrderItem model. The caller's identity comes from the token.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.FindFirstValue(ClaimTypes.Name);
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var items = request.Items.Select(i => new OrderRequestItem(i.CatalogItemId, i.Quantity)).ToList();
        var orderId = await service.PlaceOrderAsync(request.BuyerId, items, default);
        return Results.Ok(new PlaceOrderResponse(orderId));
    }
}
