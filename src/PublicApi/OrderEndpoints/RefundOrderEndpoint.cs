using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using CoreRefundRequest = Microsoft.eShopWeb.ApplicationCore.Interfaces.RefundOrderRequest;

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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderPaymentService orders) =>
                await HandleAsync(orderId, request, orders))
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orders) =>
        HandleAsync(0, request, orders);

    private async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request, IOrderPaymentService orders)
    {
        var buyerId = _httpContextAccessor.HttpContext!.RequireBuyerId();
        var (order, refund) = await orders.RefundAsync(new CoreRefundRequest(
            orderId,
            buyerId,
            request.Amount,
            request.IdempotencyKey));

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Refund = RefundDto.From(refund),
            Order = OrderDto.From(order, _paymentSettings.Currency)
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
    }
}
