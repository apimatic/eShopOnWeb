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
/// POST /api/orders/{orderId}/cancel — operator action. Cancels the order before fulfilment,
/// releasing the shopper's held funds so no money ever moves. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IPaymentOrderService service,
                CancellationToken ct) =>
            {
                try
                {
                    var order = await service.CancelAsync(orderId, ct);
                    return Results.Ok(new { orderId = order.Id, order = OrderDto.From(order) });
                }
                catch (Exception ex)
                {
                    return PaymentProblems.ToResult(ex);
                }
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderPaymentEndpoints");
    }
}
