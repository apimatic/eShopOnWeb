using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Exceptions;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the signed-in shopper's saved cards. Only safe descriptions are returned.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, OrderPaymentService>
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
            (OrderPaymentService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderPaymentService service)
    {
        var buyerId = GetBuyerId();
        var cards = await service.ListCardsAsync(buyerId, CancellationToken.None);
        var response = new PaymentMethodListResponse(System.Guid.NewGuid());

        foreach (var card in cards)
        {
            response.PaymentMethods.Add(new PaymentMethodResponse
            {
                PaymentMethodId = card.Id,
                Brand = card.Brand,
                Last4 = card.Last4,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                CardType = card.CardType
            });
        }

        return Results.Ok(response);
    }

    private string GetBuyerId()
    {
        var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "NO_IDENTITY",
                "The access token does not contain a user identity.");
        }

        return name!;
    }
}
