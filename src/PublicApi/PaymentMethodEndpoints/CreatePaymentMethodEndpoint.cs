using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ISavedPaymentMethodService methods, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, methods, buyerId);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods) =>
        HandleAsync(request, methods, string.Empty);

    private async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods, string buyerId)
    {
        var card = request.Card;
        var details = new PayPalCardDetails(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            card.BillingAddress == null ? null : new PayPalBillingAddress(
                card.BillingAddress.CountryCode,
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.PostalCode));

        var saved = await methods.SaveCardAsync(buyerId, details);
        var dto = new PaymentMethodDto
        {
            PaymentMethodId = saved.Id,
            LastDigits = saved.LastDigits,
            Brand = saved.Brand,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = dto
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
