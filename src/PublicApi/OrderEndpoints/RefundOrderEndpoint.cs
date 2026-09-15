using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest body, IOrderPaymentService payments) =>
                await HandleAsync(new RefundOrderRouteRequest(orderId, body), payments))
            .Produces<RefundCreatedResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRouteRequest request, IOrderPaymentService payments)
    {
        var buyerId = CallerIdentity.GetBuyerId(_httpContextAccessor.HttpContext);
        var order = await payments.RefundAsync(buyerId, request.OrderId, request.Body.Amount, request.Body.IdempotencyKey);
        var refund = order.FindRefundByIdempotencyKey(request.Body.IdempotencyKey);
        var response = new RefundCreatedResponse
        {
            RefundId = refund?.PaypalRefundId ?? string.Empty,
            OrderId = order.Id,
            Status = refund?.Status ?? string.Empty,
            Amount = refund?.Amount ?? 0,
            Currency = refund?.Currency ?? string.Empty,
            RemainingRefundable = order.RemainingRefundable(),
            OrderStatus = order.Status.ToString()
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{response.RefundId}", response);
    }
}

public class RefundOrderRouteRequest
{
    public RefundOrderRouteRequest(int orderId, RefundOrderRequest body)
    {
        OrderId = orderId;
        Body = body;
    }

    public int OrderId { get; }
    public RefundOrderRequest Body { get; }
}
