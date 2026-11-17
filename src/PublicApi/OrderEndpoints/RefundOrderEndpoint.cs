using System.Linq;
using System.Security.Claims;
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

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, HttpRequest httpRequest,
                IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = CreateOrderEndpoint.GetBuyerId(user);
                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    idempotencyKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault();
                }

                var refund = await paymentService.RefundOrderAsync(
                    orderId,
                    buyerId,
                    request.Amount,
                    idempotencyKey ?? string.Empty,
                    cancellationToken);

                return Results.Created($"api/orders/{orderId}/refunds/{refund.FinixRefundId}", RefundResponse.From(refund));
            })
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
