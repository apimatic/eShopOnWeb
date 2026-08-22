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
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderPaymentService service, HttpContext httpContext) =>
            {
                request.OrderId = orderId;
                request.BuyerId = httpContext.RequireBuyerId();
                request.IsAdministrator = httpContext.IsAdministrator();
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        var (order, refund) = await service.RefundAsync(
            request.OrderId,
            request.BuyerId,
            request.IsAdministrator,
            request.IdempotencyKey,
            request.Amount);

        var response = new RefundOrderResponse
        {
            RefundId = refund.PayPalRefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Order = OrderDtoMapper.ToDto(order)
        };

        return Results.Created($"api/orders/{order.Id}/refunds/{refund.PayPalRefundId}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
}

public class RefundOrderResponse
{
    public string RefundId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public OrderDto Order { get; set; } = new();
}
