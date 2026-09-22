using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total with a one-off card or a saved card.
/// Money is not taken here. Idempotent: a double-click does not authorize twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, HttpContext http) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, http);
            })
            .Produces<PaymentView>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, HttpContext http)
    {
        try
        {
            var buyerId = PaymentEndpointHelpers.GetBuyerId(http);
            var svc = http.RequestServices.GetRequiredService<IPaymentOrchestrationService>();

            var input = new PayInput
            {
                SavedPaymentMethodId = request.SavedPaymentMethodId,
                Card = request.Card is null ? null : new CardInput
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    CardholderName = request.Card.CardholderName,
                    BillingAddressLine1 = request.Card.BillingAddressLine1,
                    BillingCity = request.Card.BillingCity,
                    BillingState = request.Card.BillingState,
                    BillingPostalCode = request.Card.BillingPostalCode,
                    BillingCountryCode = request.Card.BillingCountryCode,
                },
            };

            var result = await svc.PayAsync(buyerId, request.OrderId, input, http.RequestAborted);
            return Results.Ok(result);
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }
}

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public PayCardRequest? Card { get; set; }
}

public class PayCardRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
    public string? BillingCountryCode { get; set; }
}
