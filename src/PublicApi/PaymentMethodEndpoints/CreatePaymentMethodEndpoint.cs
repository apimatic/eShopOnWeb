using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper and returns its safe description.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, HttpContext, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext http, IPaymentMethodService service) =>
                await HandleAsync(request, http, service))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext http, IPaymentMethodService service) =>
        PaymentApiHelpers.RunAsync(http, async buyerId =>
        {
            var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), http.RequestAborted);

            var response = new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = saved.Id,
                CardBrand = saved.CardBrand,
                LastFourDigits = saved.LastFourDigits,
                Expiry = saved.Expiry
            };
            return Results.Created($"api/payment-methods/{saved.Id}", response);
        });
}
