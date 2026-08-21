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

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodApiRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodApiRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<SavePaymentMethodApiResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodApiRequest request, IOrderPaymentService service)
    {
        var card = ToCard(request.Card);
        var saved = await service.SavePaymentMethodAsync(request.BuyerId!, card);
        var dto = PaymentMethodMapper.ToDto(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", new SavePaymentMethodApiResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = dto
        });
    }

    private static CardPaymentDetails ToCard(CardDetailsRequest card)
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
                card.BillingAddress.CountryCode ?? "US");
        }

        return new CardPaymentDetails(card.Number, card.Expiry, card.SecurityCode, card.Name, billing);
    }
}

public partial class SavePaymentMethodApiRequest
{
    internal string? BuyerId { get; set; }
}
