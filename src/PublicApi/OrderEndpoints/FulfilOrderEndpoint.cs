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
/// Operator action: marks the order fulfilled and captures the held funds.
/// A stale authorization is renewed before capturing; one that can no longer be
/// renewed is reported in terms an operator can act on.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderResponse, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderResponse(Guid.NewGuid()), orderPaymentService, http, orderId, ct);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderResponse request, IOrderPaymentService orderPaymentService) =>
        HandleAsync(request, orderPaymentService, httpContext: null, orderId: 0, CancellationToken.None);

    public async Task<IResult> HandleAsync(FulfilOrderResponse request, IOrderPaymentService orderPaymentService, HttpContext? httpContext, int orderId, CancellationToken ct)
    {
        var order = await orderPaymentService.FulfilOrderAsync(orderId, ct);
        return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Payment = PayOrderEndpoint.ToPaymentState(order)
        });
    }
}
