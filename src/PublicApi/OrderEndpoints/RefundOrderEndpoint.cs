using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount to refund. Omit for a full refund of the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key never refunds twice.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundResponse
{
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = default!;
    public OrderDto Order { get; set; } = default!;
}

/// <summary>
/// POST /api/orders/{orderId}/refunds — refund a fulfilled order's captured payment, in full or in
/// part, under a caller-supplied idempotency key. Shopper-scoped to the caller's own order.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, user);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new InvalidPaymentRequestException("A refund requires a caller-supplied idempotencyKey.");
        }
        if (request.Amount is <= 0m)
        {
            throw new InvalidPaymentRequestException("A refund amount, when supplied, must be greater than zero.");
        }

        var refund = await service.RefundAsync(request.OrderId, buyerId, request.Amount, request.IdempotencyKey);

        // Re-read the order so the response reflects the updated payment/refund state.
        var orders = await service.GetMyOrdersAsync(buyerId);
        var order = orders.First(o => o.Id == request.OrderId);

        var response = new RefundResponse
        {
            RefundId = refund.Id,
            Refund = refund.ToDto(),
            Order = order.ToDto()
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}
