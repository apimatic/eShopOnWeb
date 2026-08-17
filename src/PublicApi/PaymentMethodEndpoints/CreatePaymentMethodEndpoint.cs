using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>Card to save. Full card details are sent to PayPal's vault and never stored here.</summary>
    public CardDto Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }
    public SavedPaymentMethodDto PaymentMethod { get; set; } = new();
}

/// <summary>
/// POST /api/payment-methods — save a card for the signed-in shopper. The response identifies the
/// saved card and describes it safely (brand, last four, expiry) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service) =>
                await HandleAsync(request, user, service))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    private static async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null) return Results.Unauthorized();

        try
        {
            var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails());
            var response = new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = saved.Id,
                PaymentMethod = SavedPaymentMethodDto.From(saved)
            };
            return Results.Created($"api/payment-methods/{saved.Id}", response);
        }
        catch (Exception ex) when (PaymentErrorMapper.TryMap(ex, out var result))
        {
            return result;
        }
    }
}
