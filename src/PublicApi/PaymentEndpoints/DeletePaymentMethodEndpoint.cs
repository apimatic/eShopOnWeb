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

/// <summary>Removes one of the signed-in shopper's saved cards.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, IPaymentService paymentService, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                await paymentService.DeletePaymentMethodAsync(
                    EndpointUser.RequireUserName(httpContext),
                    paymentMethodId,
                    cancellationToken);

                var response = new DeletePaymentMethodResponse(System.Guid.NewGuid())
                {
                    PaymentMethodId = paymentMethodId,
                    Deleted = true,
                    Status = "Deleted",
                    Message = "The saved payment method was removed."
                };

                return Results.Ok(response);
            })
            .Produces<DeletePaymentMethodResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }
}
