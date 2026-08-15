using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Pay request: either raw card details for a one-off payment, or the id of one of the shopper's
/// saved cards. Card details are used to authorize once and are never stored or logged.
/// </summary>
public class PayOrderRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="SavedPaymentMethodId"/>.</summary>
    public CardInput? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }
}

public class CardInput
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM form, as PayPal expects.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public BillingAddressInput? BillingAddress { get; set; }
}

public class BillingAddressInput
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) the order total against a one-off card or a saved card. The amount PayPal
/// holds equals the order total to the cent. Idempotent: a double-click does not authorize twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PayPalSettings _settings;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService,
        IHttpContextAccessor httpContextAccessor, PayPalSettings settings)
    {
        _orderPaymentService = orderPaymentService;
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();

        CardDetails? card = request.Card is null ? null : new CardDetails(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode,
            request.Card.Name,
            request.Card.BillingAddress is null ? null : new CardBillingAddress(
                request.Card.BillingAddress.AddressLine1,
                request.Card.BillingAddress.AddressLine2,
                request.Card.BillingAddress.AdminArea2,
                request.Card.BillingAddress.AdminArea1,
                request.Card.BillingAddress.PostalCode,
                request.Card.BillingAddress.CountryCode));

        var instrument = new PaymentInstrument(card, request.SavedPaymentMethodId);
        var order = await _orderPaymentService.PayAsync(request.OrderId, buyerId, instrument);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToDto(order, _settings.Currency)
        });
    }
}
