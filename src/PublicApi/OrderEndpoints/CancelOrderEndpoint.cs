using System;
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
/// Operator action: cancels the order before fulfilment. Any held funds are released,
/// so no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderResponse, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderResponse(Guid.NewGuid()), orderPaymentService, http, orderId, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderResponse request, IOrderPaymentService orderPaymentService) =>
        HandleAsync(request, orderPaymentService, httpContext: null, orderId: 0, CancellationToken.None);

    public async Task<IResult> HandleAsync(CancelOrderResponse request, IOrderPaymentService orderPaymentService, HttpContext? httpContext, int orderId, CancellationToken ct)
    {
        var order = await orderPaymentService.CancelOrderAsync(orderId, ct);
        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Payment = PayOrderEndpoint.ToPaymentState(order)
        });
    }
}
