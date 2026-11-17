using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
    public string? Number { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Cvc { get; set; }
    public string? Name { get; set; }
    public AddressRequest? Address { get; set; }

    public CardRequest? ResolveCard()
    {
        if (Card is not null)
        {
            return Card;
        }

        if (string.IsNullOrWhiteSpace(Number))
        {
            return null;
        }

        return new CardRequest
        {
            Number = Number!,
            ExpirationMonth = ExpirationMonth ?? 0,
            ExpirationYear = ExpirationYear ?? 0,
            Cvc = Cvc ?? string.Empty,
            Name = Name ?? string.Empty,
            Address = Address
        };
    }
}

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public SavePaymentMethodEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (SavePaymentMethodRequest request, HttpContext httpContext) => await HandleAsync(request, httpContext))
            .Produces<SavedPaymentMethodDto>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, HttpContext httpContext)
    {
        var card = request.ResolveCard();
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        var saved = await _paymentService.SavePaymentMethodAsync(
            new SavePaymentMethodData(httpContext.User.GetBuyerId(), card.ToCardData()!),
            httpContext.RequestAborted);
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", saved);
    }
}
