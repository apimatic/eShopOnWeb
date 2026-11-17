using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Removes one of the signed-in shopper's saved cards.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string paymentMethodId, IOrderPaymentService service) =>
                await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, service))
            .Produces(204)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IOrderPaymentService service)
    {
        var http = _httpContextAccessor.HttpContext;
        var result = await service.DeletePaymentMethodAsync(
            http?.User?.Identity?.Name,
            request.PaymentMethodId,
            http?.RequestAborted ?? default);
        return EndpointResults.From(result);
    }
}
