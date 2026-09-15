using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the signed-in shopper and returns its safe display detail.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
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
            (CreatePaymentMethodRequest request, ISavedCardService savedCardService) =>
                await HandleAsync(request, savedCardService))
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();

        if (request.Card == null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            throw new PaymentException("Card details are required to save a payment method.");
        }

        var saved = await savedCardService.SaveCardAsync(buyerId, request.Card.ToCardInput());
        var dto = PaymentMethodDto.From(saved);
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", dto);
    }
}
