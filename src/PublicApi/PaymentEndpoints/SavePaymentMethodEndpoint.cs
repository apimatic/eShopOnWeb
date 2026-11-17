using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardCommand request, HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                var result = await paymentService.SaveCardAsync(buyerId, request, cancellationToken);

                return Results.Created($"api/payment-methods/{result.PaymentMethodId}", new PaymentMethodResponse
                {
                    PaymentMethodId = result.PaymentMethodId,
                    Brand = result.Brand,
                    Last4 = result.Last4,
                    ExpirationMonth = result.ExpirationMonth,
                    ExpirationYear = result.ExpirationYear,
                    CardholderName = result.CardholderName,
                    FinixPaymentInstrumentId = result.FinixPaymentInstrumentId
                });
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }
}
