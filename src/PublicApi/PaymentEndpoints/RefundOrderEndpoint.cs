using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext context,
                IPaymentApplicationService service) =>
            {
                request.OrderId = orderId;
                request.BuyerId = PaymentEndpointUser.GetBuyerId(context);
                request.IsAdministrator = PaymentEndpointUser.IsAdministrator(context);
                return await HandleAsync(request, service, context.Request.Headers["Idempotency-Key"]);
            })
            .Produces<RefundOperationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentApplicationService service) =>
        HandleAsync(request, service, default);

    private async Task<IResult> HandleAsync(RefundOrderRequest request,
        IPaymentApplicationService service, string? headerIdempotencyKey)
    {
        var command = request.ToCommand(headerIdempotencyKey);
        var refund = await service.RefundOrderAsync(request.BuyerId!, request.IsAdministrator,
            request.OrderId, command);
        return Results.Ok(refund);
    }
}
