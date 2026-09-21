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

/// <summary>
/// POST /api/payment-methods — saves (vaults) a card for the signed-in shopper. The response describes the
/// card safely (brand, last four, expiry) and returns its identifier as a top-level <c>paymentMethodId</c>.
/// Full card details are never stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedCardService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service, HttpContext http)
    {
        if (request?.Card is null)
        {
            return Results.BadRequest(new { message = "Card details are required to save a payment method." });
        }

        var buyerId = http.BuyerId();
        var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), http.RequestAborted);
        var response = saved.ToResponse();
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
