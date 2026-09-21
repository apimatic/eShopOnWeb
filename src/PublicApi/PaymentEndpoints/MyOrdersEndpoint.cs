using System.Security.Claims;
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

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IOrderPaymentService service,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var buyerId = PaymentApiSupport.RequireBuyerId(user);
                var orders = await service.GetMyOrdersAsync(buyerId, ct);
                return Results.Ok(orders);
            }))
            .WithTags("Payments");
    }
}
