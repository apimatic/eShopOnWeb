using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user, HttpContext http) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var key = request.IdempotencyKey
                          ?? http.Request.Headers["Idempotency-Key"].FirstOrDefault()
                          ?? http.Request.Headers["PayPal-Request-Id"].FirstOrDefault();
                var refund = await service.RefundAsync(orderId, buyerId, request.Amount, key ?? string.Empty);
                var body = PaymentEndpointHelpers.ToRefundResponse(refund);
                return Results.Created($"api/orders/{orderId}/refunds/{body.RefundId}", body);
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service)
        => Task.FromResult(Results.BadRequest());
}
