using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, ClaimsPrincipal user, HttpRequest httpRequest, IOrderPaymentService service) =>
            {
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(orderId, request, user, service);
            })
            .Produces<RefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(0, request, new ClaimsPrincipal(), service);

    private async Task<IResult> HandleAsync(
        int orderId,
        RefundOrderRequest request,
        ClaimsPrincipal user,
        IOrderPaymentService service)
    {
        var result = await service.RefundAsync(
            orderId,
            user.RequireBuyerId(),
            request.Amount,
            request.IdempotencyKey);
        return Results.Ok(RefundResponse.From(result, request.CorrelationId()));
    }
}
