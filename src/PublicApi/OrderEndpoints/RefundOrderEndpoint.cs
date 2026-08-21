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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orders, HttpContext httpContext) =>
            {
                request ??= new RefundOrderRequest();
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new PaymentException(400, "IdempotencyKey is required.");
                }

                var response = new RefundOrderResponse(request.CorrelationId());
                var refund = await orders.RefundAsync(
                    httpContext.RequireBuyerId(),
                    orderId,
                    request.Amount,
                    request.IdempotencyKey,
                    httpContext.RequestAborted);
                response.RefundId = refund.Id;
                response.Refund = OrderDtoMapper.From(refund);
                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orders)
        => Task.FromResult(Results.BadRequest());
}
