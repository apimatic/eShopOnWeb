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
/// Removes one of the caller's saved cards and disables it at Finix.
/// </summary>
public sealed class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext httpContext, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                    await paymentService.DeletePaymentMethodAsync(buyerId, paymentMethodId, cancellationToken);

                    return Results.NoContent();
                });
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }
}
