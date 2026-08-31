using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Raw card details for a one-off payment. Mutually exclusive with SavedCardId.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of a saved card (from POST /api/payment-methods) to pay with instead.</summary>
    public int? SavedCardId { get; set; }
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty; // YYYY-MM
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public bool AlreadyPaid { get; set; }
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Authorizes (holds) the order total with PayPal. The money is not taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public PayOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;

        var result = await _orderPaymentService.PayAsync(request.OrderId, buyerId, MapCard(request.Card), request.SavedCardId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            OrderStatus = result.Order.Status.ToString(),
            AlreadyPaid = result.AlreadyPaid,
            Payment = PaymentDto.FromEntity(result.Payment)
        };
        return Results.Ok(response);
    }

    internal static CardPaymentDetails? MapCard(CardDetailsRequest? card)
    {
        if (card is null)
        {
            return null;
        }

        return new CardPaymentDetails
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            CardholderName = card.CardholderName,
            BillingAddress = card.BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea2 = card.BillingAddress.City,
                AdminArea1 = card.BillingAddress.State,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
        };
    }
}
