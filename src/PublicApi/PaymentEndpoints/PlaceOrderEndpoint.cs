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
/// POST /api/orders — place an order from catalog items for the signed-in shopper. The order starts awaiting
/// payment. Returns the new order's id as a top-level <c>orderId</c> field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, HttpContext http, IPaymentService payments) =>
            {
                try
                {
                    using var cts = PaymentEndpointSupport.CreateBudget(http);
                    var buyerId = PaymentEndpointSupport.GetBuyerId(http);
                    var result = await payments.PlaceOrderAsync(
                        buyerId,
                        PaymentEndpointSupport.ToLineInputs(request.Items),
                        PaymentEndpointSupport.ToShippingInput(request.ShipTo),
                        cts.Token);
                    return Results.Created($"api/orders/{result.OrderId}", result);
                }
                catch (Exception ex)
                {
                    return PaymentEndpointSupport.ToResult(ex);
                }
            })
            .Produces<PlaceOrderResult>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
