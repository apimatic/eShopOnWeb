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

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Operator action: cancels the order before fulfilment, releasing the shopper's held funds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken) =>
            {
                var payment = await orderPaymentService.CancelOrderAsync(orderId, cancellationToken);
                return Results.Ok(new CancelOrderResponse
                {
                    OrderId = orderId,
                    Status = "Cancelled",
                    Payment = payment == null ? null : PaymentDto.FromEntity(payment)
                });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
