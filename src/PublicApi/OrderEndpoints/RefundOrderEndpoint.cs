using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ICheckoutPaymentService checkout, ClaimsPrincipal user) =>
            {
                var order = await checkout.RefundAsync(
                    orderId,
                    OrderEndpointHelpers.GetBuyerId(user),
                    OrderEndpointHelpers.IsAdministrator(user),
                    request.IdempotencyKey,
                    request.Amount);

                var refund = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
                return Results.Ok(new RefundOrderResponse
                {
                    RefundId = refund?.Id ?? 0,
                    OrderId = order.Id,
                    Refund = refund == null ? null : new OrderRefundDto
                    {
                        RefundId = refund.Id,
                        PayPalRefundId = refund.PayPalRefundId,
                        Status = refund.Status,
                        Amount = refund.Amount,
                        Currency = refund.Currency,
                        IdempotencyKey = refund.IdempotencyKey,
                        CreatedAt = refund.CreatedAt
                    },
                    Order = OrderEndpointHelpers.ToDto(order)
                });
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutPaymentService checkout) =>
        Task.FromResult(Results.BadRequest());
}
