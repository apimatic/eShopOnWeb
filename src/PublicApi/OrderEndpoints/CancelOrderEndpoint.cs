using System.Security.Claims;
using System.Threading;
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
/// Operator action: cancels an order before fulfilment and releases the card hold.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CancelOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CancelOrderRequest request, ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderPaymentService.CancelOrderAsync(
            request.OrderId, cancellationToken);

        return Results.Ok(OrderResponse.From(order, request.CorrelationId(),
            "Order canceled and any card hold released."));
    }
}
