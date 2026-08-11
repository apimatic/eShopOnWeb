using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured order (the caller's own), in full or in part, under a caller-supplied
/// idempotency key. Repeating the same key returns the same refund; two distinct partial refunds of
/// the same capture are legitimate; the total refunded never exceeds what was captured. Returns the
/// refund id as a top-level field.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private readonly IPaymentConfiguration _paymentConfiguration;

    public RefundOrderEndpoint(IPaymentConfiguration paymentConfiguration)
    {
        _paymentConfiguration = paymentConfiguration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotencyKey is required for refunds.");
        }
        if (request.Amount is <= 0)
        {
            return Results.BadRequest("Refund amount must be positive when specified.");
        }

        var (order, refund) = await service.RefundOrderAsync(request.BuyerId, request.OrderId,
            request.Amount, request.IdempotencyKey);

        var response = new RefundOrderResponse
        {
            RefundId = refund.RefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Order = OrderMapper.ToSummary(order, _paymentConfiguration.Currency)
        };
        return Results.Created($"api/orders/{order.Id}/refunds/{refund.RefundId}", response);
    }
}
