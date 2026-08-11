using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved
/// card and describes it safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
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
            (SavePaymentMethodRequest request, IPaymentMethodService paymentMethodService) =>
                await HandleAsync(request, paymentMethodService))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var buyerId = CallerIdentity.BuyerId(_httpContextAccessor.HttpContext!);

        if (request?.Card is null)
        {
            throw new PaymentException("Card details are required to save a card.", 400);
        }

        var card = PaymentRequestMapper.ToCardDetails(request.Card);
        var method = await paymentMethodService.SaveCardAsync(buyerId, card, request.Alias);

        var response = new SavePaymentMethodResponse(method.Id, PaymentMapper.ToDto(method));
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}
