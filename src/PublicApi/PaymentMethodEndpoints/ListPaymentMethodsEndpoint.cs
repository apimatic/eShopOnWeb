using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>The caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(user, service, ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service)
        => HandleAsync(user, service, default);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct)
    {
        var buyerId = user.BuyerId();
        var cards = await service.GetSavedCardsAsync(buyerId, ct);

        var response = new ListPaymentMethodsResponse();
        foreach (var card in cards)
        {
            response.PaymentMethods.Add(SavedCardDto.From(card));
        }
        return Results.Ok(response);
    }
}
