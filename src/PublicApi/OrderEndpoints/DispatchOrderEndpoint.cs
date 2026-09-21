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
/// Operator action: mark an order dispatched. Tells the shopper it is on its way and queues the
/// "how did delivery go?" follow-up with the provider for a few days later. Admin only.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderMessagingService _orderMessagingService;

    public DispatchOrderEndpoint(IOrderMessagingService orderMessagingService)
    {
        _orderMessagingService = orderMessagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        using var cts = EndpointBudget.Start();
        var found = await _orderMessagingService.DispatchOrderAsync(orderId, cts.Token);
        return found ? Results.Ok(new { orderId, status = "dispatched" }) : Results.NotFound();
    }
}
