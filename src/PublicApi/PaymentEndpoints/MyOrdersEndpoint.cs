using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/my-orders — the caller's own orders with their payment state (shopper-scoped).</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        async (ClaimsPrincipal user, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = CallerContext.BuyerId(user);
                var orders = await service.GetMyOrdersAsync(buyerId, ct);
                return Results.Ok(new MyOrdersResponse { Orders = orders });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }
}
