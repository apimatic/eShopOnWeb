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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, HttpContext httpContext, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(orderId, request, checkout, httpContext.User);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderCheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderCheckoutService checkout, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name
            ?? throw new ApplicationCore.Exceptions.PaymentException("The caller identity is missing.", 401);

        var order = await checkout.RefundAsync(buyerId, orderId, request.IdempotencyKey, request.Amount);
        var refund = order.FindRefundByIdempotencyKey(request.IdempotencyKey)
            ?? throw new ApplicationCore.Exceptions.PaymentException("Refund was created but could not be loaded.", 502);

        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            OrderId = orderId,
            Order = OrderDto.From(order)
        };

        return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
    }
}
