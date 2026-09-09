using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it at PayPal. Returns the saved card's id as a
/// top-level <c>paymentMethodId</c> plus a safe description. No card number is ever stored here.
/// </summary>
public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequest request, ClaimsPrincipal user, CancellationToken ct, ISavedCardService service) =>
            {
                request.UserId = user.GetUserId();
                request.Cancellation = ct;
                return await HandleAsync(request, service);
            })
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, ISavedCardService service)
    {
        if (request.Card is null)
        {
            throw new InvalidPaymentOperationException("Card details are required to save a card.");
        }

        var method = await service.SaveCardAsync(request.UserId, request.Card.ToCardDetails(),
            request.Alias, request.Cancellation);

        var response = new SaveCardResponse(request.CorrelationId())
        {
            PaymentMethodId = method.Id,
            Card = SavedCardDto.From(method)
        };
        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}

public class SaveCardRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
    public string? Alias { get; set; }

    [JsonIgnore] public string UserId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken Cancellation { get; set; }
}

public class SaveCardResponse : BaseResponse
{
    public SaveCardResponse(System.Guid correlationId) : base(correlationId) { }
    public SaveCardResponse() { }

    public int PaymentMethodId { get; set; }
    public SavedCardDto Card { get; set; } = new();
}
