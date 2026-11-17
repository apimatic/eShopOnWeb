using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards.
/// </summary>
public class GetPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext,
             IOrderPaymentService paymentService,
             CancellationToken cancellationToken) =>
            {
                var methods = await paymentService.GetSavedPaymentMethodsAsync(
                    httpContext.User.GetBuyerId(),
                    cancellationToken);

                return Results.Ok(methods);
            })
            .Produces<IReadOnlyList<SavedPaymentMethodDto>>()
            .WithTags("PaymentMethodEndpoints");
    }
}
