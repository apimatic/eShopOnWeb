using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card in Finix for the signed-in shopper and returns a safe, recognisable description.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentService);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var saved = await paymentService.SavePaymentMethodAsync(request.BuyerId, new SavePaymentMethodCommand
        {
            Alias = request.Alias,
            Card = new CardCommand
            {
                Number = request.Card.Number,
                ExpirationMonth = request.Card.ExpirationMonth,
                ExpirationYear = request.Card.ExpirationYear,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.Name,
                Address = new ShippingAddressCommand
                {
                    Street = request.Card.Address?.Street,
                    City = request.Card.Address?.City,
                    State = request.Card.Address?.State,
                    Country = request.Card.Address?.Country,
                    ZipCode = request.Card.Address?.ZipCode
                }
            }
        });

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            Alias = saved.Alias,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            ExpirationMonth = saved.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear
        });
    }
}
