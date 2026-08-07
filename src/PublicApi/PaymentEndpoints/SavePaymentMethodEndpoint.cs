using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper so later orders can be paid without re-entering it.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SavePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, IPaymentMethodService paymentMethodService) =>
                await HandleAsync(request, paymentMethodService))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request?.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            return Results.BadRequest(new { message = "Card details are required to save a payment method." });
        }

        try
        {
            var saved = await paymentMethodService.SaveCardAsync(buyerId, request.Card.ToCardDetails());

            var response = new SavePaymentMethodResponse
            {
                PaymentMethodId = saved.Id,
                PaymentMethod = PaymentMethodDto.FromEntity(saved)
            };

            return Results.Created($"api/payment-methods/{saved.Id}", response);
        }
        catch (PaymentFailedException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status402PaymentRequired, title: "Saving card failed");
        }
    }
}

/// <summary>Request body for saving a card.</summary>
public class SavePaymentMethodRequest
{
    /// <summary>The card to save. Full details are sent to PayPal's vault and never stored by this application.</summary>
    public CardModel? Card { get; set; }
}

/// <summary>Response for a saved card. <see cref="PaymentMethodId"/> is the top-level identifier.</summary>
public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
