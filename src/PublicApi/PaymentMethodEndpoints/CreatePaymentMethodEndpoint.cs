using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, HttpContext httpContext, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(request, checkout, httpContext.User);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IOrderCheckoutService checkout) =>
        HandleAsync(request, checkout, null);

    private async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IOrderCheckoutService checkout, ClaimsPrincipal? user)
    {
        var buyerId = user?.Identity?.Name
            ?? throw new ApplicationCore.Exceptions.PaymentException("The caller identity is missing.", 401);

        var card = ToCard(request.Card);
        var method = await checkout.SaveCardAsync(buyerId, card);
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            PaymentMethod = PaymentMethodDto.From(method)
        };

        return Results.Created($"api/payment-methods/{method.Id}", response);
    }

    internal static CardPaymentSource ToCard(CardDetailsDto card)
    {
        CardBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode);
        }

        return new CardPaymentSource(card.Number, card.Expiry, card.SecurityCode, card.Name, billing);
    }
}
