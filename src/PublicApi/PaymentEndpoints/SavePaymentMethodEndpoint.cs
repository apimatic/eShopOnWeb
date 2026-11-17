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

/// <summary>Saves a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, IPaymentService paymentService, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                var result = await paymentService.SavePaymentMethodAsync(
                    EndpointUser.RequireUserName(httpContext),
                    request.ResolveCard(),
                    cancellationToken);

                return Results.Created(
                    $"/api/payment-methods/{result.PaymentMethodId}",
                    SavePaymentMethodResponse.From(result, request.CorrelationId()));
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }
}
