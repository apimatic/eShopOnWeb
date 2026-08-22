using System.Security.Claims;
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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, HttpRequest httpRequest, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                request.IdempotencyKey = httpRequest.GetIdempotencyKey(request.IdempotencyKey);
                return await HandleAsync(request, checkout);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutService checkout)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new CheckoutException(400, "A refund idempotency key is required (body idempotencyKey or Idempotency-Key header).");
        }

        var refund = await checkout.RefundAsync(request.BuyerId, request.OrderId, request.Amount, request.IdempotencyKey);
        var payment = await checkout.GetPaymentAsync(request.OrderId);
        var response = new RefundOrderResponse
        {
            RefundId = refund.Id,
            Refund = OrderDtoMapper.MapRefund(refund),
            Payment = payment is null ? null : OrderDtoMapper.MapPayment(payment)
        };

        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class RefundOrderResponse
{
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
    public PaymentDto? Payment { get; set; }
}
