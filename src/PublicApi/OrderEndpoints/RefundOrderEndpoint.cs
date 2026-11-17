using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refund a captured payment in full or in part. Repeated calls with the same idempotencyKey return the original refund.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, PaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, PaymentService paymentService) =>
            {
                var actionRequest = new RefundOrderRequest(orderId)
                {
                    Amount = request.Amount,
                    IdempotencyKey = request.IdempotencyKey
                };
                return await HandleAsync(actionRequest, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, PaymentService paymentService)
    {
        try
        {
            var ownerId = EndpointHelpers.GetOwnerId(_httpContextAccessor.HttpContext!.User);
            var (payment, refund) = await paymentService.RefundOrderAsync(
                ownerId,
                request.OrderId,
                request.Amount,
                request.IdempotencyKey);

            var response = new RefundOrderResponse(request.CorrelationId())
            {
                OrderId = payment.OrderId,
                RefundId = refund.FinixTransferId,
                Refund = RefundDto.From(refund),
                Order = OrderPaymentDto.From(payment)
            };

            return Results.Created($"api/orders/{payment.OrderId}/refunds/{refund.FinixTransferId}", response);
        }
        catch (PaymentOperationException ex)
        {
            return EndpointHelpers.ToResult(ex);
        }
    }
}
