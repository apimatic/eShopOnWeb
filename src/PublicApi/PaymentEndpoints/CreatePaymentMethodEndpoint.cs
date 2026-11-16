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
/// Saves a card for the signed-in shopper and returns a safe, non-sensitive descriptor.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IFinixPaymentService>
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
                (SavePaymentMethodRequest request, IFinixPaymentService paymentService) =>
                {
                    return await HandleAsync(request, paymentService);
                })
            .Produces<PaymentMethodDto>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IFinixPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext);
        var paymentMethod = await paymentService.SavePaymentMethodAsync(buyerId, request);
        return Results.Created($"api/payment-methods/{paymentMethod.PaymentMethodId}", paymentMethod);
    }
}
