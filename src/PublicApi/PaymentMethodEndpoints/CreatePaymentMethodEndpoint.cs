using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    throw new PaymentException(401, "A signed-in shopper is required.");
                }

                var saved = await paymentService.SavePaymentMethodAsync(
                    buyerId,
                    new SavedCardDetails
                    {
                        Number = request.Card?.CardNumber ?? request.Card?.Number
                            ?? request.CardNumber ?? request.Number ?? string.Empty,
                        ExpirationMonth = request.Card is not null && request.Card.ExpirationMonth != 0
                            ? request.Card.ExpirationMonth
                            : request.Card?.ExpiryMonth ?? (request.ExpirationMonth != 0 ? request.ExpirationMonth : request.ExpiryMonth ?? 0),
                        ExpirationYear = request.Card is not null && request.Card.ExpirationYear != 0
                            ? request.Card.ExpirationYear
                            : request.Card?.ExpiryYear ?? (request.ExpirationYear != 0 ? request.ExpirationYear : request.ExpiryYear ?? 0),
                        SecurityCode = request.Card?.SecurityCode ?? request.Card?.Cvv ?? request.Card?.Cvc
                            ?? request.SecurityCode ?? request.Cvv ?? request.Cvc,
                        Name = request.Card?.Name ?? request.Name,
                        Address = request.Card?.Address ?? request.Address
                    },
                    cancellationToken);

                var response = new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    Last4 = saved.Last4,
                    ExpirationMonth = saved.ExpirationMonth,
                    ExpirationYear = saved.ExpirationYear,
                    CardType = saved.CardType,
                    Name = saved.Name,
                    CreatedAt = saved.CreatedAt
                };

                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
