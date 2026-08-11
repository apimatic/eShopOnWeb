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

/// <summary>The caller's saved cards, described safely.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, IPaymentMethodService>
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
            (IPaymentMethodService paymentMethodService) =>
                await HandleAsync(paymentMethodService))
            .Produces<PaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentMethodService paymentMethodService)
    {
        var buyerId = CallerIdentity.BuyerId(_httpContextAccessor.HttpContext!);
        var cards = await paymentMethodService.GetCardsAsync(buyerId);
        var response = new PaymentMethodsResponse(cards.Select(PaymentMapper.ToDto).ToList());
        return Results.Ok(response);
    }
}
