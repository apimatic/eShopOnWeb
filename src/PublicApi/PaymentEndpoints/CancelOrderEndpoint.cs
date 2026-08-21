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
/// Operator action: cancels the order before fulfilment, releasing the shopper's held funds so no
/// money ever moved. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken ct) =>
            {
                var order = await paymentService.CancelOrderAsync(orderId, ct);

                var response = new CancelOrderResponseDto
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString()
                };
                return Results.Ok(response);
            })
            .Produces<CancelOrderResponseDto>()
            .WithTags("PaymentEndpoints");
    }
}
