using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and describes it
/// safely (brand + last digits) — never full card details, which are not stored by this application.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service) =>
            {
                request.CallerName = user.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service)
    {
        if (string.IsNullOrEmpty(request.CallerName))
        {
            return Results.Unauthorized();
        }

        var card = PaymentMappers.ToCardDetails(request.Card);
        var saved = await service.SaveCardAsync(request.CallerName, card);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMappers.ToDto(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto? Card { get; set; }

    [JsonIgnore]
    public string? CallerName { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public SavedCardDto PaymentMethod { get; set; } = new();
}
