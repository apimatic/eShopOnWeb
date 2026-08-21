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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderPaymentService orders) =>
                await HandleAsync(orderId, request, orders))
            .Produces<RefundCreatedResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orders)
        => HandleAsync((int)_httpContextAccessor.HttpContext!.Request.RouteValues["orderId"]!, request, orders);

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.RequireBuyerId();
        var refund = await orders.RefundAsync(orderId, buyerId, request.Amount, request.IdempotencyKey);
        var order = await orders.GetBuyerOrderAsync(orderId, buyerId);
        var response = new RefundCreatedResponse
        {
            RefundId = refund.Id,
            OrderId = orderId,
            PayPalRefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Payment = order is null ? new PaymentStateResponse() : OrderResponseMapper.PaymentFrom(order)
        };
        return Results.Created($"api/orders/{orderId}/refunds/{response.RefundId}", response);
    }
}
