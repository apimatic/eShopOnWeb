using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Issues a full refund of an order's PayPal payment. Idempotent in effect: a double-submit never
/// results in a double refund. On success the order reflects the Refunded state.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http) => await HandleAsync(orderId, http))
            .Produces<PaymentStatusResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext http)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);
        var paymentService = http.RequestServices.GetRequiredService<IPaymentService>();

        var result = await paymentService.RefundOrderAsync(buyerId, orderId, http.RequestAborted);

        return Results.Ok(PaymentStatusResponse.From(result, Guid.NewGuid()));
    }
}
