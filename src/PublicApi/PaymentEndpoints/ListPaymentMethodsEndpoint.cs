using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The caller's saved cards, described safely.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService savedCardService) =>
                await HandleAsync(savedCardService))
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.BuyerId();
        var cards = await savedCardService.GetCardsAsync(buyerId!);
        return Results.Ok(cards);
    }
}
