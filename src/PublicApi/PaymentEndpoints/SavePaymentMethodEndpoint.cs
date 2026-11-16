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

public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (SavePaymentMethodRequest request, IPaymentApplicationService payments,
                    ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var buyerId = PaymentEndpointHelpers.RequireBuyerId(user);
                        var card = new CardInput(
                            request.Name,
                            request.Number,
                            request.ExpirationMonth,
                            request.ExpirationYear,
                            request.SecurityCode,
                            request.Street,
                            request.City,
                            request.State,
                            request.Country,
                            request.ZipCode);

                        var saved = await payments.SaveCardAsync(buyerId, card, cancellationToken);

                        return Results.Created(
                            $"api/payment-methods/{saved.PaymentMethodId}",
                            new SavePaymentMethodResponse
                            {
                                PaymentMethodId = saved.PaymentMethodId,
                                Brand = saved.Brand,
                                Last4 = saved.Last4,
                                ExpirationMonth = saved.ExpirationMonth,
                                ExpirationYear = saved.ExpirationYear
                            });
                    }
                    catch (PaymentOperationException exception)
                    {
                        return PaymentEndpointHelpers.Fail(exception);
                    }
                })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }
}
