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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>GET /api/payment-methods — the caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                ISavedPaymentMethodService paymentMethodService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(user);
                var methods = await paymentMethodService.GetForBuyerAsync(buyerId, cancellationToken);

                return Results.Ok(new
                {
                    paymentMethods = methods.Select(m => m.ToDto()).ToList()
                });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("PaymentMethodEndpoints");
    }
}
