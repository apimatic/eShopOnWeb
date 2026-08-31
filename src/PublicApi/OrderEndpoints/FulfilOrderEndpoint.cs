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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: fulfils the order and captures the held funds. The response
/// reports what PayPal reported: captured amount, PayPal's fee and net proceeds.
/// A stale authorization is renewed automatically; if it cannot be renewed the
/// error says so in operator-actionable terms.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken ct) =>
            {
                var payment = await paymentService.FulfilOrderAsync(orderId, ct);

                var response = new FulfilOrderResponse
                {
                    OrderId = orderId,
                    OrderStatus = "Fulfilled",
                    Payment = PaymentDto.FromPayment(payment)
                };
                return Results.Ok(response);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
