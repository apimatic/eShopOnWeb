using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total with a one-off card or a saved card.
/// Does not take the money yet. Shopper-scoped to the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, HttpContext http, IPaymentService payments) =>
            {
                try
                {
                    using var cts = PaymentEndpointSupport.CreateBudget(http);
                    var buyerId = PaymentEndpointSupport.GetBuyerId(http);
                    var input = new PayInput(PaymentEndpointSupport.ToCardInput(request.Card), request.SavedPaymentMethodId);
                    var result = await payments.PayAsync(buyerId, orderId, input, cts.Token);
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    return PaymentEndpointSupport.ToResult(ex);
                }
            })
            .Produces<PaymentActionResult>()
            .WithTags("PaymentEndpoints");
    }
}
