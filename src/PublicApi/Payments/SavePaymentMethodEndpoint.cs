using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and describes it
/// safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, HttpContext http, IPaymentService paymentService) =>
            {
                request.BuyerId = user.GetBuyerId();
                request.Cancellation = http.RequestAborted;
                return await HandleAsync(request, paymentService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
    {
        var card = (request.Card ?? new CardDto()).ToDomain();
        var paymentMethodId = await paymentService.SavePaymentMethodAsync(request.BuyerId, card, request.Cancellation);

        // Read the saved card's safe description back so the shopper can recognise it.
        var saved = (await paymentService.GetPaymentMethodsAsync(request.BuyerId, request.Cancellation))
            .FirstOrDefault(m => m.Id == paymentMethodId);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethodId,
            CardBrand = saved?.CardBrand ?? string.Empty,
            LastFourDigits = saved?.LastFourDigits ?? string.Empty,
            Expiry = saved?.Expiry ?? string.Empty
        };
        return Results.Created($"api/payment-methods/{paymentMethodId}", response);
    }
}

public class SavePaymentMethodRequest : PaymentRequestBase
{
    public CardDto? Card { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(System.Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
}
