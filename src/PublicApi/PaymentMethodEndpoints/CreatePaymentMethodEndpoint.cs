using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card in PayPal's vault for the signed-in shopper. Full card details are never stored by this app.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var card = new CardDetails(
            request.Number,
            request.ExpiryYearMonth,
            request.SecurityCode,
            request.CardholderName,
            request.AddressLine1,
            request.AddressLine2,
            request.City,
            request.State,
            request.PostalCode,
            request.CountryCode);

        var paymentMethod = await paymentMethodService.SavePaymentMethodAsync(request.BuyerId, card, request.Alias);

        response.PaymentMethodId = paymentMethod.Id;
        response.PaymentMethod = new PaymentMethodDto
        {
            PaymentMethodId = paymentMethod.Id,
            CardBrand = paymentMethod.CardBrand,
            Last4 = paymentMethod.Last4,
            Expiry = paymentMethod.Expiry,
            Alias = paymentMethod.Alias,
            CreatedAt = paymentMethod.CreatedAt
        };
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
