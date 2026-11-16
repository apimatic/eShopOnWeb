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
/// Saves a card for the signed-in shopper. The card is tokenized by Finix and
/// only non-sensitive display data is stored by eShop.
/// </summary>
public sealed class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext httpContext, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                    var saved = await paymentService.SavePaymentMethodAsync(buyerId,
                        request.Card.ToPaymentCardDetails(), cancellationToken);

                    return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", saved);
                });
            })
            .Produces<SavedCardInfo>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("PaymentMethodEndpoints");
    }
}
