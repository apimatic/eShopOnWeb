using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Issues a full refund of an order's PayPal payment. Idempotent: a repeated call
/// never produces a second refund. On success the order reflects "Refunded".
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                var request = new RefundOrderRequest { OrderId = orderId, BuyerId = user.GetBuyerId() };
                return await HandleAsync(request, paymentService, ct);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
        => HandleAsync(request, paymentService, default);

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService, CancellationToken ct)
    {
        var order = await paymentService.RefundOrderAsync(request.OrderId, request.BuyerId, ct);
        return Results.Ok(new RefundOrderResponse(request.CorrelationId()) { Order = OrderDto.From(order) });
    }
}
