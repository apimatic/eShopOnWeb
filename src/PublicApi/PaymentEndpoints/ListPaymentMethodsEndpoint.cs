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
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentService>
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
                (IPaymentService paymentService) =>
                {
                    return await HandleAsync(paymentService);
                })
            .Produces<PaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyerId(_httpContextAccessor);
        var cards = await paymentService.GetSavedCardsAsync(buyerId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(new PaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentDtoMapper.ToResponse).ToList()
        });
    }
}
