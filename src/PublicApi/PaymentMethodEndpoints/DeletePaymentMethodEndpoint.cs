using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest>
{
    private readonly IPayPalPaymentService _payPal;

    public DeletePaymentMethodEndpoint(IPayPalPaymentService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string paymentMethodId, HttpContext ctx, CancellationToken ct) =>
            {
                var request = new DeletePaymentMethodRequest
                {
                    BuyerId = ctx.User.Identity?.Name ?? string.Empty,
                    PaymentMethodId = paymentMethodId
                };
                return await HandleAsync(request);
            })
            .Produces(204)
            .Produces(403)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        // Ownership check: caller must own this payment token
        System.Collections.Generic.IReadOnlyList<SavedCardInfo> callerCards;
        try
        {
            callerCards = await _payPal.ListSavedCardsAsync(merchantCustomerId: request.BuyerId);
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Failed to verify card ownership",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }

        var owned = callerCards.Any(c => c.PaymentTokenId == request.PaymentMethodId);
        if (!owned)
        {
            // 404 instead of 403 to avoid leaking existence
            return Results.NotFound();
        }

        try
        {
            await _payPal.DeleteSavedCardAsync(paymentTokenId: request.PaymentMethodId);
            return Results.NoContent();
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Failed to delete payment method",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
}
