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
/// Operator action: marks the order fulfilled, which is when the held money is actually captured. A
/// stale hold is renewed rather than failing the fulfilment. Restricted to the administrator role.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken ct) =>
            {
                var order = await paymentService.FulfilOrderAsync(orderId, ct);
                var payment = order.Payment;

                var response = new FulfilOrderResponseDto
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    CaptureId = payment?.CaptureId ?? string.Empty,
                    CapturedAmount = payment?.CapturedAmount ?? 0m,
                    PayPalFee = payment?.PayPalFee,
                    NetAmount = payment?.NetAmount,
                    Currency = payment?.Currency ?? string.Empty
                };
                return Results.Ok(response);
            })
            .Produces<FulfilOrderResponseDto>()
            .WithTags("PaymentEndpoints");
    }
}
