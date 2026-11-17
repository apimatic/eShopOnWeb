using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public RefundOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, RefundOrderRequest request, HttpContext httpContext) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, httpContext);
                })
            .Produces<RefundDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, HttpContext httpContext)
    {
        var data = new RefundOrderData(request.OrderId,
            httpContext.User.GetBuyerId(),
            httpContext.User.IsAdministrator(),
            PaymentRequestMapping.ToAmountCents(request.Amount, request.AmountCents),
            request.IdempotencyKey);
        var refund = await _paymentService.RefundOrderAsync(data, httpContext.RequestAborted);
        return Results.Ok(refund);
    }
}
