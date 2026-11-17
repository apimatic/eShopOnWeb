using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Saves a card for the signed-in shopper.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IOrderPaymentService>
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
            (SavePaymentMethodRequest request, IOrderPaymentService service) => await HandleAsync(request, service))
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IOrderPaymentService service)
    {
        var http = _httpContextAccessor.HttpContext;
        var idempotencyKey = http?.Request.Headers["Idempotency-Key"].ToString();
        var result = await service.SavePaymentMethodAsync(
            http?.User?.Identity?.Name,
            request,
            idempotencyKey,
            http?.RequestAborted ?? default);
        return EndpointResults.From(result);
    }
}
