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

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }

    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action: mark the order fulfilled, which is when the held
/// funds are captured. A stale hold is renewed first; the payment then shows PayPal's captured amount,
/// fee and net proceeds. Administrator-only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var payment = await service.FulfilAsync(orderId, ct);
                var response = new FulfilOrderResponse(Guid.NewGuid())
                {
                    Payment = PaymentStateDto.From(payment)
                };
                return Results.Ok(response);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("PaymentEndpoints");
    }
}
