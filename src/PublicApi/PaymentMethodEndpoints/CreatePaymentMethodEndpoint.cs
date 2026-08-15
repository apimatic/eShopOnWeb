using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// POST /api/payment-methods — save (vault) a card for the signed-in shopper. Returns a safe
/// description of the saved card and its id (top-level field). Full card details are never stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService service) =>
                await HandleAsync(request, service))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService service)
    {
        var buyerId = CurrentUser.RequireBuyerId(_http);
        var card = CardMapping.ToInput(request.Card);
        var saved = await service.SaveCardAsync(buyerId, card, CurrentUser.RequestAborted(_http));

        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.CardBrand,
            LastFourDigits = saved.LastFourDigits,
            ExpiryYearMonth = saved.ExpiryYearMonth,
            CardholderName = saved.CardholderName,
            CreatedAt = saved.CreatedAt
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
