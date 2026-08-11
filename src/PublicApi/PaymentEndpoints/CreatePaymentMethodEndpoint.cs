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

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>A friendly label for the saved card. Optional; a safe default is used if omitted.</summary>
    public string? Alias { get; set; }

    public CardDto Card { get; set; } = new();

    [JsonIgnore]
    public string CallerUsername { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>The saved card's id, returned as a top-level field.</summary>
    public int PaymentMethodId { get; set; }
    public string? Alias { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response describes the card safely
/// (brand, last four, expiry) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service) =>
            {
                request.CallerUsername = CallerIdentity.RequireUsername(user);
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service)
    {
        var saved = await service.SaveCardAsync(request.CallerUsername, request.Alias ?? string.Empty, request.Card.ToCardDetails());

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Alias = saved.Alias,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            Expiry = saved.Expiry
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
