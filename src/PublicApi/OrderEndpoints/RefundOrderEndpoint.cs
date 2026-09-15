using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _paymentService;

    public RefundOrderEndpoint(IOrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest? request, HttpRequest httpRequest, ClaimsPrincipal user) =>
            {
                request ??= new RefundOrderRequest();
                var key = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    ? request.IdempotencyKey
                    : httpRequest.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
                return await HandleAsync(new RefundOrderRouteRequest(orderId, request, key), user);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRouteRequest routeRequest, ClaimsPrincipal user)
    {
        var buyerId = user.RequireUserName();
        if (string.IsNullOrWhiteSpace(routeRequest.IdempotencyKey))
        {
            throw new PaymentException(400, "A caller-supplied idempotency key is required. Send idempotencyKey in the body or an Idempotency-Key header.");
        }

        var order = await _paymentService.RefundAsync(
            routeRequest.OrderId,
            buyerId,
            routeRequest.Body.Amount,
            routeRequest.IdempotencyKey);

        var refund = order.FindRefundByIdempotencyKey(routeRequest.IdempotencyKey)
                     ?? order.Refunds.Last();

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency,
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            RefundableRemaining = order.RefundableRemaining()
        });
    }
}

public record RefundOrderRouteRequest(int OrderId, RefundOrderRequest Body, string IdempotencyKey);
