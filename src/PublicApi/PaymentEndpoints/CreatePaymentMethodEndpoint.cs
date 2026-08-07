using System.Security.Claims;
using System.Text.Json.Serialization;
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

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>The identifier of the saved card (top-level, so callers can drive the flow).</summary>
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>Saves a card for the signed-in shopper via PayPal's Vault.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService service, CancellationToken ct) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service)
        => HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service, CancellationToken ct)
    {
        if (request.Card is null || !request.Card.HasCoreDetails)
        {
            return Results.BadRequest(new { message = "Card number, expiry (YYYY-MM) and security code are required." });
        }

        var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardPaymentDetails(), ct);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMethodDto.From(saved)
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
