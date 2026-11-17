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
/// Saves a card for the signed-in shopper. Only a safe description (brand and last four) is returned/stored.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, OrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SavePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, OrderPaymentService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, OrderPaymentService service)
    {
        var buyerId = GetBuyerId();
        var card = await service.SaveCardAsync(buyerId, request, CancellationToken.None);

        var response = new PaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.Last4,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            CardType = card.CardType
        };

        return Results.Created($"api/payment-methods/{card.Id}", response);
    }

    private string GetBuyerId()
    {
        var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "NO_IDENTITY", "The access token does not contain a user identity.");
        }

        return name!;
    }
}
