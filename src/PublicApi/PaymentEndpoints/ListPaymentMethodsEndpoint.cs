using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (IPaymentApplicationService payments, ClaimsPrincipal user,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var buyerId = PaymentEndpointHelpers.RequireBuyerId(user);
                        var cards = await payments.ListCardsAsync(buyerId, cancellationToken);

                        return Results.Ok(new ListPaymentMethodsResponse
                        {
                            PaymentMethods = cards
                                .Select(card => new PaymentMethodDto
                                {
                                    PaymentMethodId = card.PaymentMethodId,
                                    Brand = card.Brand,
                                    Last4 = card.Last4,
                                    ExpirationMonth = card.ExpirationMonth,
                                    ExpirationYear = card.ExpirationYear
                                })
                                .ToList()
                        });
                    }
                    catch (PaymentOperationException exception)
                    {
                        return PaymentEndpointHelpers.Fail(exception);
                    }
                })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentEndpoints");
    }
}
