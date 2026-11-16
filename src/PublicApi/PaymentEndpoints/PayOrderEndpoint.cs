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

public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, PayOrderRequest request, IPaymentApplicationService payments,
                    ClaimsPrincipal user, CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var buyerId = PaymentEndpointHelpers.RequireBuyerId(user);
                        var card = request.Card is null
                            ? null
                            : new CardInput(
                                request.Card.Name,
                                request.Card.Number,
                                request.Card.ExpirationMonth,
                                request.Card.ExpirationYear,
                                request.Card.SecurityCode,
                                request.Card.Street,
                                request.Card.City,
                                request.Card.State,
                                request.Card.Country,
                                request.Card.ZipCode);

                        var summary = await payments.PayAsync(
                            buyerId,
                            orderId,
                            new PayOrderCommand(request.PaymentMethodId, card),
                            cancellationToken);

                        return Results.Ok(PaymentEndpointHelpers.ToDto(summary));
                    }
                    catch (PaymentOperationException exception)
                    {
                        return PaymentEndpointHelpers.Fail(exception);
                    }
                })
            .Produces<OrderDto>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }
}
