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
/// Operator action: fulfils an order and captures the previously authorized funds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public FulfilOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, FulfilOrderRequest request, ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderPaymentService.FulfilOrderAsync(
            request.OrderId, cancellationToken);

        var message = order.Payment?.Status == "CapturePending"
            ? "Order fulfilled; the capture was submitted and settlement is still pending."
            : "Order fulfilled and payment captured.";

        return Results.Ok(OrderResponse.From(order, request.CorrelationId(), message));
    }
}
