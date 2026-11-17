using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Refunds a captured payment, in full or in part.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId, RefundOrderRequest request) =>
            {
                return await HandleAsync(orderId, request);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request) => HandleAsync(0, request);

    public async Task<IResult> HandleAsync(int orderId, RefundOrderRequest request)
    {
        var user = _httpContextAccessor.HttpContext!.User;
        var callerId = user.GetBuyerId();
        var refund = await _paymentService.RefundOrderAsync(callerId, user.IsOperator(), orderId, request.ToInput());

        var order = await _paymentService.GetOrderAsync(orderId);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.RefundId,
            OrderId = orderId,
            Refund = PaymentDtoMapper.ToDto(refund),
            Payment = order?.Payment is null ? null : PaymentDtoMapper.ToDto(order.Payment)
        };

        return Results.Ok(response);
    }
}
