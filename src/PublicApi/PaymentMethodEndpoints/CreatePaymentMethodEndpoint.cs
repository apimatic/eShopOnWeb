using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, HttpContext http, ISavedPaymentMethodService methods) =>
            {
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, methods);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods)
    {
        var card = request.Card ?? new CardDetailsRequest();
        var saved = await methods.SaveAsync(
            request.BuyerId,
            new CardPaymentInput
            {
                Name = card.Name ?? string.Empty,
                Number = card.Number ?? string.Empty,
                Expiry = card.Expiry ?? string.Empty,
                SecurityCode = card.SecurityCode ?? string.Empty,
                BillingAddress = new CardBillingAddressInput
                {
                    CountryCode = card.BillingAddress?.CountryCode ?? string.Empty,
                    AddressLine1 = card.BillingAddress?.AddressLine1,
                    AddressLine2 = card.BillingAddress?.AddressLine2,
                    AdminArea2 = card.BillingAddress?.AdminArea2,
                    AdminArea1 = card.BillingAddress?.AdminArea1,
                    PostalCode = card.BillingAddress?.PostalCode
                }
            },
            default);

        return Results.Created($"api/payment-methods/{saved.PaymentTokenId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentTokenId,
            LastDigits = saved.LastDigits,
            Brand = saved.Brand,
            Expiry = saved.Expiry,
            Name = saved.CardholderName
        });
    }
}
