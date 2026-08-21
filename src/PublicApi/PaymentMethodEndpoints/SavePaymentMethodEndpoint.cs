using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    [JsonIgnore] public string CallerId { get; set; } = string.Empty;

    public CardDetailsPayload Card { get; set; } = new();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>The saved card's identifier (top-level, so the flow can be driven end to end).</summary>
    public int PaymentMethodId { get; set; }

    public SavedCardDto Card { get; set; } = new();
}

/// <summary>Saves (vaults) a card for the signed-in shopper. The card number is only ever sent to PayPal.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.CallerId = user.GetUserName();
                return await HandleAsync(request, service, ct);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service, CancellationToken ct)
    {
        var card = request.Card;
        if (string.IsNullOrWhiteSpace(card.Number) || card.ExpiryMonth is < 1 or > 12 || card.ExpiryYear < 1)
        {
            return Results.BadRequest(new { message = "Valid card number, expiry month and expiry year are required." });
        }

        var details = new PayPalCardDetails
        {
            Number = card.Number,
            Expiry = $"{card.ExpiryYear.ToString("D4", CultureInfo.InvariantCulture)}-{card.ExpiryMonth.ToString("D2", CultureInfo.InvariantCulture)}",
            SecurityCode = card.SecurityCode,
            CardholderName = card.CardholderName
        };

        var saved = await service.SaveCardAsync(details, request.CallerId, ct);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Card = SavedCardDto.FromEntity(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
