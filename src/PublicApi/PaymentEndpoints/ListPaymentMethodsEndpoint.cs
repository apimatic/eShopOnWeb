using System.Linq;
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

/// <summary>GET /api/payment-methods — the caller's saved cards (safe description only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IOrderPaymentService service,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var buyerId = PaymentApiSupport.RequireBuyerId(user);
                var methods = await service.GetPaymentMethodsAsync(buyerId, ct);
                return Results.Ok(methods.Select(PaymentMappings.ToSavedCard).ToList());
            }))
            .WithTags("Payments");
    }
}
