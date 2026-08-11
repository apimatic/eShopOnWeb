using System.Collections.Generic;
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

/// <summary>Lists the signed-in shopper's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var orders = await service.GetMyOrdersAsync(buyerId, ct);
                return Results.Ok(orders);
            })
            .Produces<IReadOnlyList<OrderPaymentSummary>>()
            .WithTags("PaymentEndpoints");
    }
}
