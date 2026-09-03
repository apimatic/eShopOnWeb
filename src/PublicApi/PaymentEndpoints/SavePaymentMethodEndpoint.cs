using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response describes the card safely
/// (brand + last four + expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, CardDetailsDto, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public SavePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CardDetailsDto request, IPaymentService service) => await HandleAsync(request, service))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(CardDetailsDto request, IPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var card = request.ToCardInput()!;
        var saved = await service.SaveCardAsync(ctx.User.BuyerId(), card, ctx.RequestAborted);
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        });
    }
}
