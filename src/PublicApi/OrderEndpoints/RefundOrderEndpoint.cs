using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(orderId, request, service, user, cancellationToken);
            })
            .Produces<RefundOrderResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(
        int orderId,
        RefundOrderRequest request,
        IOrderPaymentService service,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await service.RefundAsync(orderId, buyerId, request.IdempotencyKey, request.Amount, cancellationToken);
        var refund = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (refund is null)
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Refund = new RefundDto
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Currency = refund.Currency,
                Status = refund.Status
            },
            Order = OrderPaymentDto.From(order)
        };
        return Results.Ok(response);
    }
}
