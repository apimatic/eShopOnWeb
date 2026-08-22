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

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ICheckoutPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, http);
            })
            .Produces<CreateRefundResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutPaymentService service) =>
        HandleAsync(request, service, null!);

    private async Task<IResult> HandleAsync(RefundOrderRequest request, ICheckoutPaymentService service, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new CheckoutException("idempotencyKey is required for refunds.");
        }

        var buyerId = EndpointIdentity.RequireUserName(http);
        var refund = await service.RefundAsync(
            request.OrderId,
            buyerId,
            request.IdempotencyKey,
            request.Amount,
            http.RequestAborted);

        var response = new CreateRefundResponse
        {
            RefundId = refund.Id,
            OrderId = request.OrderId,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Currency = refund.CurrencyCode
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{response.RefundId}", response);
    }
}
