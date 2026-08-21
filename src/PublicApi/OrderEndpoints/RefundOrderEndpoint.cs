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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _paymentSettings;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings paymentSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException(400, "IdempotencyKey is required.");
        }

        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = Caller.Name(httpContext);
        var refund = await paymentService.RefundAsync(
            request.OrderId,
            buyerId,
            request.IdempotencyKey.Trim(),
            request.Amount,
            httpContext.RequestAborted);

        var order = await paymentService.GetBuyerOrderAsync(request.OrderId, buyerId, httpContext.RequestAborted);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Amount = refund.Amount,
            Status = refund.Status,
            Order = order is null ? new OrderDto() : OrderDto.From(order, _paymentSettings.Currency)
        });
    }
}
