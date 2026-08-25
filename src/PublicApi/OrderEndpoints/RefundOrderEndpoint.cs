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

/// <summary>Refunds a fulfilled order's captured payment, in full or in part, for the shopper who owns it.</summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId());

        var result = await orderPaymentService.RefundOrderAsync(request.OrderId, request.BuyerId, request.Amount, request.IdempotencyKey);
        if (result is null)
        {
            return Results.NotFound();
        }

        var (order, refund) = result.Value;
        response.RefundId = refund.Id;
        response.Order = order.ToDto();
        return Results.Ok(response);
    }
}
