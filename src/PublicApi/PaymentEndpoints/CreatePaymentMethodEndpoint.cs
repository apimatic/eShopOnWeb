using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Card == null)
                {
                    return Results.BadRequest(new BlazorShared.Models.ErrorDetails
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        Message = "Card details are required.",
                        Code = "CARD_REQUIRED"
                    });
                }

                var command = new SaveCardCommand
                {
                    Alias = request.Alias,
                    Card = new CardPaymentDetails
                    {
                        Number = request.Card.Number,
                        ExpirationMonth = request.Card.ExpirationMonth,
                        ExpirationYear = request.Card.ExpirationYear,
                        SecurityCode = request.Card.SecurityCode,
                        Name = request.Card.Name,
                        Address = request.Card.Address == null ? null : new PaymentAddress
                        {
                            Line1 = request.Card.Address.Line1 ?? "123 Main St",
                            Line2 = request.Card.Address.Line2,
                            City = request.Card.Address.City ?? "San Francisco",
                            Region = request.Card.Address.Region ?? "CA",
                            PostalCode = request.Card.Address.PostalCode ?? "94105",
                            Country = request.Card.Address.Country ?? "USA"
                        }
                    }
                };

                var card = await service.SaveAsync(buyerId, command, cancellationToken);
                var response = new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = card.Id,
                    PaymentMethod = PaymentDtoMapper.Map(card)
                };

                return Results.Created($"api/payment-methods/{card.Id}", response);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
