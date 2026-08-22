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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderCheckoutService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, request, service, user);
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService service)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderCheckoutService service, ClaimsPrincipal user)
    {
        var (order, refund) = await service.RefundAsync(orderId, user.GetBuyerId(), request.Amount, request.IdempotencyKey);
        return Results.Ok(new
        {
            refundId = refund.Id,
            payPalRefundId = refund.PayPalRefundId,
            status = refund.Status,
            amount = refund.Amount,
            currency = refund.Currency,
            order = order.ToResponse()
        });
    }
}
