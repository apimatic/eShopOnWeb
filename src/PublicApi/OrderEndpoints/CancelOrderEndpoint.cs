using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancel an order. Calls off any not-yet-sent delivery follow-up so it can never
/// reach the shopper, and tells the shopper the order was cancelled. Admin only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderMessagingService _orderMessagingService;

    public CancelOrderEndpoint(IOrderMessagingService orderMessagingService)
    {
        _orderMessagingService = orderMessagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        using var cts = EndpointBudget.Start();
        var found = await _orderMessagingService.CancelOrderAsync(orderId, cts.Token);
        return found ? Results.Ok(new { orderId, status = "cancelled" }) : Results.NotFound();
    }
}
