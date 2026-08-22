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

public class RefundOrderEndpoint : IEndpoint<IResult, CreateRefundRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateRefundRequest request, ClaimsPrincipal user, HttpRequest httpRequest, IOrderPaymentService service) =>
            {
                var buyerId = CreateOrderEndpoint.RequireBuyerId(user);
                var idempotencyKey = request.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey)
                    && httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    idempotencyKey = headerKey.ToString();
                }

                var refund = await service.RefundAsync(orderId, buyerId, request.Amount, idempotencyKey);
                return Results.Ok(OrderApiMapper.ToRefund(refund));
            })
            .Produces<RefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateRefundRequest request, IOrderPaymentService requestHandler)
        => Task.FromResult(Results.BadRequest());
}
