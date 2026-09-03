using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; the held funds are captured.
/// A stale authorization is renewed first. Administrator-only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IPaymentOrderService service,
                PayPalSettings settings,
                System.Threading.CancellationToken ct) =>
            {
                var order = await service.FulfilAsync(orderId, ct);
                return Results.Ok(OrderPaymentResponse.From(order, settings.Currency));
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }
}
