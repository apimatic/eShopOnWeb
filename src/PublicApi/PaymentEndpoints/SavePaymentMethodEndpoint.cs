using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodRequest
{
    public CardDto? Card { get; set; }
}

/// <summary>POST /api/payment-methods — saves (vaults) a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _http;

    public SavePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedPaymentMethodService service) =>
                await HandleAsync(request, service))
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedPaymentMethodService service) =>
        PaymentEndpointSupport.ExecuteAsync(async () =>
        {
            var buyerId = PaymentEndpointSupport.RequireUserName(_http);
            var ct = PaymentEndpointSupport.RequestAborted(_http);
            var card = PaymentEndpointSupport.ToCardDetails(request.Card);
            if (card is null)
                throw new PaymentFlowException(PaymentFlowError.Validation, "Card number and expiry (YYYY-MM) are required.");

            var view = await service.SaveCardAsync(buyerId, card, ct);
            return Results.Created($"api/payment-methods/{view.PaymentMethodId}", new
            {
                paymentMethodId = view.PaymentMethodId,
                brand = view.Brand,
                lastFourDigits = view.LastFourDigits,
                expiryMonthYear = view.ExpiryMonthYear,
                cardholderName = view.CardholderName,
                createdAt = view.CreatedAt
            });
        });
}
