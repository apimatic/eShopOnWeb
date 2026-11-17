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
using Microsoft.eShopWeb.Infrastructure.Finix;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : CardDetailsRequest
{
    [JsonIgnore]
    public string? BuyerId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    public CardDetailsRequest? Card { get; set; }
    public CardDetailsRequest? CardDetails { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }

    public int? ResolvePaymentMethodId() => PaymentMethodId ?? SavedPaymentMethodId;

    public CardDetailsRequest? ResolveCard()
    {
        if (Card != null)
        {
            return Card;
        }

        if (CardDetails != null)
        {
            return CardDetails;
        }

        return string.IsNullOrWhiteSpace(Number ?? CardNumber) ? null : this;
    }
}

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    private readonly FinixOptions _finixOptions;

    public PayOrderEndpoint(IOptions<FinixOptions> finixOptions)
    {
        _finixOptions = finixOptions.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = user.GetBuyerId();
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var card = request.ResolveCard()?.ToCardPaymentInput();
        var details = await paymentService.PayOrderAsync(
            request.BuyerId!,
            request.OrderId,
            card,
            request.ResolvePaymentMethodId(),
            CancellationToken.None);

        return Results.Ok(details.ToOrderResponse(_finixOptions.Currency));
    }
}
