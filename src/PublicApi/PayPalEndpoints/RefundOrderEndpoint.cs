using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>Refunds the caller's captured order, in full or in part, under a caller-supplied idempotency key.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, IOrderPaymentService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CallerIdentity.BuyerId(http);
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("PayPalPayments");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var result = await service.RefundAsync(request.BuyerId, request.OrderId, request.Amount,
            request.IdempotencyKey, request.Ct);
        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = result.RefundId,
            Amount = result.Amount,
            Status = result.Status,
        });
    }
}
