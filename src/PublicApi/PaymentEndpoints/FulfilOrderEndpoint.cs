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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds — that is when the money is
/// actually taken. A stale hold is renewed before capture; one that cannot be renewed is reported.
/// Restricted to administrators.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, int, IPaymentOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, service, ct);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(int orderId, IPaymentOrderService service) =>
        HandleAsync(orderId, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(int orderId, IPaymentOrderService service, CancellationToken ct)
    {
        var order = await service.FulfilAsync(orderId, ct);
        var response = new OrderActionResponse
        {
            OrderId = order.Id,
            Order = order.ToDto()
        };
        return Results.Ok(response);
    }
}
