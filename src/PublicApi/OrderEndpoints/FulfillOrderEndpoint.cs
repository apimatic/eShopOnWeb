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
/// Operator: mark the order fulfilled. This is when the held money is actually captured.
/// A stale authorization is renewed automatically when possible.
/// </summary>
public class FulfillOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var payment = await paymentService.FulfillOrderAsync(orderId, cancellationToken);

                var response = new FulfillOrderResponse
                {
                    OrderId = orderId,
                    Payment = PaymentDto.FromPayment(payment)
                };
                return Results.Ok(response);
            })
            .Produces<FulfillOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class FulfillOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
