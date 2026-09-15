using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardRequest? Card { get; set; }
}

/// <summary>A saved card, described safely enough to recognise — never full card details.</summary>
public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>Saves a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Saves a card for the signed-in shopper", Tags = new[] { "PaymentMethodEndpoints" })]
            async (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                if (request.Card is null || !request.Card.IsPopulated())
                    throw new PaymentValidationException("Card details are required to save a payment method.");

                var buyerId = user.BuyerId();
                var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails());

                var dto = new PaymentMethodDto
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.CardBrand,
                    Last4 = saved.Last4,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName
                };
                return Results.Created($"api/payment-methods/{saved.Id}", dto);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
