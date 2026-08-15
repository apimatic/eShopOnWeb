using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Shopper action. Lists the caller's saved cards (safe descriptions only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, EmptyRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _http;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService savedCardService) =>
                await HandleAsync(new EmptyRequest(), savedCardService))
            .Produces<PaymentMethodResponse[]>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest _, ISavedCardService savedCardService)
    {
        var buyerId = EndpointCaller.RequireBuyerId(_http);
        var cards = await savedCardService.ListCardsAsync(buyerId);
        var response = cards.Select(PaymentMapping.ToPaymentMethodResponse).ToList();
        return Results.Ok(response);
    }
}
