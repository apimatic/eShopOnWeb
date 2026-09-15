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
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures the
/// money, renewing a stale authorization first if needed. Restricted to administrators.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                var payment = await orderPaymentService.FulfilOrderAsync(orderId, cancellationToken);
                return Results.Ok(PaymentDto.From(payment));
            }))
            .Produces<PaymentDto>()
            .WithTags("OrderEndpoints");
    }
}
