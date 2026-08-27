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

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the authorized funds.
/// A stale authorization is renewed first; one that cannot be renewed fails with an actionable error.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService, CancellationToken cancellationToken) =>
            {
                var payment = await orderPaymentService.FulfilOrderAsync(orderId, cancellationToken);
                return Results.Ok(new FulfilOrderResponse
                {
                    OrderId = orderId,
                    Status = "Fulfilled",
                    Payment = PaymentDto.FromEntity(payment)
                });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
