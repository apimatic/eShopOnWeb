using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal. Full card details go to
/// PayPal only — never stored here. Responds with a safe description (paymentMethodId top-level).
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext http, ISavedCardService service) =>
            {
                request.BuyerId = PaymentMapper.GetBuyerId(http);
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service)
    {
        var input = new SaveCardInput(request.Card.ToCardDetails(), request.Alias);
        var pm = await service.SaveCardAsync(request.BuyerId, input);
        var dto = PaymentMethodDto.From(pm);
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", dto);
    }
}
