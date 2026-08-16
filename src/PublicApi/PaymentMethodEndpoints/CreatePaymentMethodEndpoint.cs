using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. The response identifies the saved card and describes it
/// safely (brand + last four) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService savedCardService) =>
                await HandleAsync(request, savedCardService))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await savedCardService.SaveCardAsync(buyerId, request.Card.ToCardDetails());
        if (!result.IsSuccess)
        {
            return result.ToProblem();
        }

        var saved = result.Value;
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = saved.ToResponse()
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
