using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and describes it
/// safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _http;

    public SavePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService service) => await HandleAsync(request, service))
            .Produces<SavedCardInfo>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
    {
        var ctx = _http.HttpContext!;
        var buyerId = CallerIdentity.BuyerId(ctx.User);

        if (request.Card is null)
            throw PaymentException.Validation("A card is required to save a payment method.");

        var info = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), ctx.RequestAborted);
        return Results.Created($"api/payment-methods/{info.PaymentMethodId}", info);
    }
}
