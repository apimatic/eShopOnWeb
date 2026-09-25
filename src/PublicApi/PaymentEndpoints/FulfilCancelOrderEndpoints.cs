using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OperatorOrderRequest
{
    public int OrderId { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action: fulfils the order and captures (takes) the
/// held funds. Restricted to the administrator role. Idempotent.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OperatorOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext ctx) =>
                await HandleAsync(new OperatorOrderRequest { OrderId = orderId, CancellationToken = ctx.RequestAborted }, service))
            .Produces<OrderDto>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OperatorOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.FulfilAsync(request.OrderId, request.CancellationToken);
        return Results.Ok(PaymentMappings.ToDto(order));
    }
}

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action: cancels a not-yet-fulfilled order and
/// releases the held funds. Restricted to the administrator role. Idempotent.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OperatorOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext ctx) =>
                await HandleAsync(new OperatorOrderRequest { OrderId = orderId, CancellationToken = ctx.RequestAborted }, service))
            .Produces<OrderDto>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OperatorOrderRequest request, IOrderPaymentService service)
    {
        var order = await service.CancelAsync(request.OrderId, request.CancellationToken);
        return Results.Ok(PaymentMappings.ToDto(order));
    }
}
