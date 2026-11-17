using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Finix;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the caller's orders together with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, FinixPaymentService payments, CancellationToken ct) =>
            {
                var shopperId = user.Identity?.Name ?? string.Empty;
                var response = await payments.GetMyOrdersAsync(shopperId, ct);
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPayments");
    }
}
