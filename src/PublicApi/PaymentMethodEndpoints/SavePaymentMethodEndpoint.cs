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

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequest Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public SavedCardDto Card { get; set; } = new();
}

/// <summary>Saves (vaults) a card for the signed-in shopper for reuse on later orders.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _http;

    public SavePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService savedCardService) =>
                await HandleAsync(request, savedCardService))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var ctx = _http.HttpContext!;
        var buyerId = ctx.User.GetBuyerId();

        var method = await savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), ctx.RequestAborted);

        var response = new SavePaymentMethodResponse()
        {
            PaymentMethodId = method.Id,
            Card = method.ToDto()
        };
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}
