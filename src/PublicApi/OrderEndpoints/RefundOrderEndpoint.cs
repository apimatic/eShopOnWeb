using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public long? AmountCents { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    [JsonIgnore]
    public int OrderId { get; set; }
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) {}
    public RefundOrderResponse() {}

    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext httpContext, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    request.IdempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
                }

                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var amountCents = request.AmountCents ??
            (request.Amount.HasValue ? Order.ToCents(request.Amount.Value) : (long?)null);

        var (order, refund) = await paymentService.RefundAsync(request.OrderId,
            amountCents,
            request.IdempotencyKey);

        return Results.Ok(new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.FinixTransferId,
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order)
        });
    }
}
