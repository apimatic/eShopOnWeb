using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(request, paymentMethods);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods)
    {
        var buyerId = BuyerIdentity.RequireBuyerId(_httpContextAccessor.HttpContext!.User);
        if (request.Card is null)
        {
            throw new PaymentException("Card details are required.");
        }

        var saved = await paymentMethods.SaveCardAsync(buyerId, MapCard(request.Card));
        var response = Map(saved);
        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }

    internal static CardPaymentSource MapCard(CardDetailsRequest card)
    {
        CardBillingAddress? billing = null;
        if (card.BillingAddress is not null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode,
                string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode) ? "US" : card.BillingAddress.CountryCode);
        }

        return new CardPaymentSource(
            card.Number?.Replace(" ", "") ?? string.Empty,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            billing);
    }

    internal static PaymentMethodResponse Map(SavedPaymentMethod saved)
    {
        return new PaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
    }
}
