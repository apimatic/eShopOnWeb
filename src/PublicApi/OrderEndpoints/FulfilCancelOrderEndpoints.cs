using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: captures the held funds and records Finix's captured amount, fee and net.
/// </summary>
public sealed class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    var order = await paymentService.FulfilOrderAsync(orderId, cancellationToken);
                    return Results.Ok(order);
                });
            })
            .Produces<OrderInfo>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("OrderEndpoints");
    }
}

/// <summary>
/// Operator action: cancels an order before fulfilment and releases any hold.
/// </summary>
public sealed class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    var order = await paymentService.CancelOrderAsync(orderId, cancellationToken);
                    return Results.Ok(order);
                });
            })
            .Produces<OrderInfo>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("OrderEndpoints");
    }
}
