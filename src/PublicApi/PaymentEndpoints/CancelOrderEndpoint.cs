using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }

    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action: cancel before fulfilment, releasing the shopper's
/// held funds so no money ever moves. Administrator-only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var payment = await service.CancelAsync(orderId, ct);
                var response = new CancelOrderResponse(Guid.NewGuid())
                {
                    Payment = PaymentStateDto.From(payment)
                };
                return Results.Ok(response);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}
