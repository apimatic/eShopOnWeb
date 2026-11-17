using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService service, CancellationToken cancellationToken) =>
            {
                var command = new RefundCommand
                {
                    Amount = request.Amount,
                    IdempotencyKey = request.IdempotencyKey
                };

                var refund = await service.RefundAsync(orderId, command, cancellationToken);
                var order = await service.GetOrderAsync(orderId, cancellationToken);
                var response = new RefundOrderResponse(request.CorrelationId())
                {
                    RefundId = refund.Id,
                    Refund = PaymentDtoMapper.Map(refund),
                    Order = order == null ? new OrderPaymentDto() : PaymentDtoMapper.Map(order)
                };

                return Results.Created($"api/orders/{orderId}/refunds/{refund.Id}", response);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
