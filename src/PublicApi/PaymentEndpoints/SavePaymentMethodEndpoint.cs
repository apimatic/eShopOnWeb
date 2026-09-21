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
/// POST /api/payment-methods — save a card for the signed-in shopper (vaulted at PayPal). The response
/// identifies the saved card and describes it safely (brand + last digits + expiry) — never full details.
/// Returns the saved card id as a top-level <c>paymentMethodId</c> field.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext http, IPaymentService payments) =>
            {
                try
                {
                    using var cts = PaymentEndpointSupport.CreateBudget(http);
                    var buyerId = PaymentEndpointSupport.GetBuyerId(http);
                    var card = PaymentEndpointSupport.ToCardInput(request.Card)
                        ?? throw new PaymentOperationException(PaymentError.InvalidRequest, "Card details are required.");
                    var result = await payments.SaveCardAsync(buyerId, new SaveCardInput(card), cts.Token);
                    return Results.Created($"api/payment-methods/{result.PaymentMethodId}", result);
                }
                catch (Exception ex)
                {
                    return PaymentEndpointSupport.ToResult(ex);
                }
            })
            .Produces<SavedCardView>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
