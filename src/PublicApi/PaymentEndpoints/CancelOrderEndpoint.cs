using System;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels before fulfilment; the held funds are released so no
/// money ever moved. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IPaymentService payments) =>
            {
                try
                {
                    using var cts = PaymentEndpointSupport.CreateBudget(http);
                    var result = await payments.CancelAsync(orderId, cts.Token);
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
