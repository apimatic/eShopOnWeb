using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Refunds a captured order in full or in part, idempotently per caller-supplied key.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var http = _httpContextAccessor.HttpContext;
        var idempotencyKey = http?.Request.Headers["Idempotency-Key"].ToString();
        var result = await service.RefundAsync(
            http?.User?.Identity?.Name,
            request.OrderId,
            request,
            idempotencyKey,
            http?.RequestAborted ?? default);
        return EndpointResults.From(result);
    }
}
