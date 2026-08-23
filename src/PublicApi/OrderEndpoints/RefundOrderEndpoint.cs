using System;
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
    private readonly IHttpContextAccessor _http;

    public RefundOrderEndpoint(IHttpContextAccessor http)
    {
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orders) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orders);
            })
            .Produces<RefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = _http.HttpContext!.RequireBuyerId();
        var refund = await orders.RefundOrderAsync(buyerId, request.OrderId, request.Amount, request.IdempotencyKey);
        return Results.Ok(new RefundResponse
        {
            RefundId = refund.Id,
            OrderId = request.OrderId,
            PaypalRefundId = refund.PaypalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.Currency
        });
    }
}
