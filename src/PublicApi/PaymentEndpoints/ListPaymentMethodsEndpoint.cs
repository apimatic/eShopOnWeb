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

/// <summary>Lists the signed-in shopper's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                var methods = await paymentService.ListPaymentMethodsAsync(
                    EndpointUser.RequireUserName(httpContext),
                    cancellationToken);

                return Results.Ok(ListPaymentMethodsResponse.From(methods, System.Guid.NewGuid()));
            })
            .Produces<ListPaymentMethodsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }
}
