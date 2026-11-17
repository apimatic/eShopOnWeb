using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : CardDetailsRequest
{
    public CardDetailsRequest? Card { get; set; }
    public CardDetailsRequest? CardDetails { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }

    public CardDetailsRequest ResolveCard() => Card ?? CardDetails ?? this;
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<SavedCardResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentService paymentService)
    {
        var card = request.ResolveCard().ToCardPaymentInput();
        var savedCard = await paymentService.SaveCardAsync(request.BuyerId!, card, CancellationToken.None);
        return Results.Created($"api/payment-methods/{savedCard.Id}", savedCard.ToSavedCardResponse());
    }
}
