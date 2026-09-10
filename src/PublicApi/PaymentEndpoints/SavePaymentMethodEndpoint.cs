using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    /// <summary>Identifier of the saved card (top-level).</summary>
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
}

/// <summary>
/// Saves a card for the signed-in shopper (vaults it at PayPal). The response identifies the
/// saved card and describes it safely — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var saved = await service.SaveCardAsync(buyerId, request.Card.ToPaymentCard(), ct);
                return Results.Created($"api/payment-methods/{saved.Id}", new SavePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    LastFourDigits = saved.LastFourDigits,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName
                });
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service) =>
        Task.FromResult(Results.Empty as IResult);
}
