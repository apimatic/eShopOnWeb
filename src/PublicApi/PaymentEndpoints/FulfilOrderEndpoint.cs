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
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentStateModel? Payment { get; set; }
}

/// <summary>
/// Operator action: fulfils the order, capturing the held funds. A hold that has gone stale is
/// renewed first; one that can no longer be renewed is reported in operator-actionable terms.
/// Idempotent in effect.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken ct) =>
            {
                var payment = await paymentService.FulfilAsync(orderId, ct);
                return Results.Ok(new FulfilOrderResponse
                {
                    OrderId = orderId,
                    Payment = PaymentStateModel.From(payment)
                });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
