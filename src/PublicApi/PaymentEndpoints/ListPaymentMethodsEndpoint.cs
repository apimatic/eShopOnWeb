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
/// GET /api/payment-methods — the caller's saved cards (safe descriptors only). Shopper-scoped.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IPaymentService payments) =>
            {
                try
                {
                    using var cts = PaymentEndpointSupport.CreateBudget(http);
                    var buyerId = PaymentEndpointSupport.GetBuyerId(http);
                    var result = await payments.GetSavedCardsAsync(buyerId, cts.Token);
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    return PaymentEndpointSupport.ToResult(ex);
                }
            })
            .Produces<System.Collections.Generic.IReadOnlyList<SavedCardView>>()
            .WithTags("PaymentEndpoints");
    }
}
