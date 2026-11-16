using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IFinixPaymentService>
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
                (IFinixPaymentService paymentService) =>
                {
                    return await HandleAsync(new ListPaymentMethodsRequest(), paymentService);
                })
            .Produces<PaymentMethodDto[]>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IFinixPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext);
        var paymentMethods = await paymentService.GetPaymentMethodsAsync(buyerId);
        return Results.Ok(paymentMethods);
    }
}
