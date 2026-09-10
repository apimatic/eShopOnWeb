using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>Saves a card for the signed-in shopper. Returns a safe description — never full card details.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext http, ISavedCardService service, CancellationToken ct) =>
            {
                request.BuyerId = CallerIdentity.BuyerId(http);
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<SavedCardResponse>(StatusCodes.Status201Created)
            .WithTags("PayPalPaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
    {
        var card = request.Card.ToCardDetails();
        var saved = await service.SaveCardAsync(request.BuyerId, card, request.Ct);
        var response = new SavedCardResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastDigits = saved.LastDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName,
            CreatedAt = saved.CreatedAt,
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
