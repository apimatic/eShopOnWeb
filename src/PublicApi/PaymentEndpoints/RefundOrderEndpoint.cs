using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds a captured payment, fully or partially. A caller-supplied
/// idempotency key prevents a retry from refunding twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, httpContext);
            })
            .Produces<RefundResult>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext httpContext)
    {
        var result = await _paymentService.RefundOrderAsync(
            CallerIdentity.GetBuyerId(httpContext),
            CallerIdentity.IsAdministrator(httpContext),
            request.OrderId,
            request.Amount,
            request.IdempotencyKey,
            httpContext.RequestAborted);

        return Results.Created($"api/orders/{request.OrderId}/refunds/{result.RefundId}", result);
    }
}
