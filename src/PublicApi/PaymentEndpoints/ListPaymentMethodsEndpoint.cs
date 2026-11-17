using System.Linq;
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

public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                var cards = await paymentService.GetSavedCardsAsync(buyerId, cancellationToken);

                return Results.Ok(new PaymentMethodListResponse
                {
                    PaymentMethods = cards.Select(card => new PaymentMethodResponse
                    {
                        PaymentMethodId = card.PaymentMethodId,
                        Brand = card.Brand,
                        Last4 = card.Last4,
                        ExpirationMonth = card.ExpirationMonth,
                        ExpirationYear = card.ExpirationYear,
                        CardholderName = card.CardholderName,
                        FinixPaymentInstrumentId = card.FinixPaymentInstrumentId
                    }).ToList()
                });
            })
            .Produces<PaymentMethodListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }
}
