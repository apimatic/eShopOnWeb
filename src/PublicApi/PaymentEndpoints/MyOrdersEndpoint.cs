using System.Collections.Generic;
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

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var orders = await service.GetMyOrdersAsync(user.BuyerId(), ct);
                return Results.Ok(orders);
            })
            .Produces<IReadOnlyList<PaymentView>>()
            .WithTags("OrderPaymentEndpoints");
    }
}
