using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRouteRequest, IPaymentCheckoutService>
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
            async (int orderId, RefundOrderRequest body, IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(new RefundOrderRouteRequest(orderId, body), payments);
            })
            .Produces<RefundCreatedResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRouteRequest request, IPaymentCheckoutService payments)
    {
        var refund = await payments.RefundAsync(
            EndpointUser.BuyerId(_httpContextAccessor.HttpContext!),
            request.OrderId,
            request.Body.Amount,
            request.Body.IdempotencyKey);

        var mapped = OrderResponseMapper.MapRefund(refund);
        var response = new RefundCreatedResponse
        {
            RefundId = mapped.RefundId,
            PayPalRefundId = mapped.PayPalRefundId,
            Status = mapped.Status,
            Amount = mapped.Amount,
            Currency = mapped.Currency,
            CreatedAt = mapped.CreatedAt
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
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
