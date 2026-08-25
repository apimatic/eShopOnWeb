using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record RefundOrderRequest(int OrderId, decimal? Amount, string IdempotencyKey);
public record RefundOrderResponse(string RefundId, string RefundStatus, string? AmountRefunded);

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refund",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, IPaymentService svc) =>
            {
                var req = request with { OrderId = orderId };
                return await HandleAsync(req, svc);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService svc)
    {
        var (refund, refundId) = await svc.RefundOrderAsync(request.OrderId, request.Amount, request.IdempotencyKey);
        return Results.Ok(new RefundOrderResponse(refundId, refund.RefundStatus, refund.AmountValue));
    }
}
