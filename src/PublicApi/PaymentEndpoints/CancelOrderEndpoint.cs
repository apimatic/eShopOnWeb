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
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentStateModel? Payment { get; set; }
}

/// <summary>
/// Operator action: cancels the order before fulfilment, releasing the shopper's held funds so
/// no money ever moved. Idempotent in effect.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken ct) =>
            {
                var payment = await paymentService.CancelAsync(orderId, ct);
                return Results.Ok(new CancelOrderResponse
                {
                    OrderId = orderId,
                    Payment = PaymentStateModel.From(payment)
                });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}
