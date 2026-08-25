using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService service, HttpContext ctx) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                bool hasCard = !string.IsNullOrEmpty(request.CardNumber);
                bool hasSavedCard = request.PaymentMethodId.HasValue;

                if (!hasCard && !hasSavedCard)
                    return Results.BadRequest("Either card details or paymentMethodId must be provided.");
                if (hasCard && hasSavedCard)
                    return Results.BadRequest("Provide either card details or paymentMethodId, not both.");

                try
                {
                    PayOrderResult result;
                    if (hasSavedCard)
                    {
                        result = await service.PayOrderWithSavedCardAsync(orderId, buyerId, request.PaymentMethodId!.Value);
                    }
                    else
                    {
                        var cardReq = new PayOrderWithCardRequest(
                            CardNumber: request.CardNumber!,
                            CardExpiryMonth: request.CardExpiryMonth!.Value,
                            CardExpiryYear: request.CardExpiryYear!.Value,
                            Cvv: request.CardCvv,
                            CardholderName: request.CardholderName,
                            BillingCountryCode: request.BillingCountryCode ?? "US",
                            BillingPostalCode: request.BillingPostalCode);
                        result = await service.PayOrderWithCardAsync(orderId, buyerId, cardReq);
                    }

                    return Results.Ok(new PayOrderResponse
                    {
                        OrderId = orderId,
                        AuthorizationId = result.AuthorizationId,
                        AuthorizationStatus = result.AuthorizationStatus,
                        AuthorizationExpiry = result.AuthorizationExpiry,
                        Currency = result.Currency,
                        Amount = result.Amount
                    });
                }
                catch (UnauthorizedAccessException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
                catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
        => await Task.FromResult(Results.StatusCode(501));
}
