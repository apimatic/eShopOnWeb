using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    /// <summary>One-off card to pay with. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequestDto? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead of entering a card.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// Authorize (hold) an order's total: pay by one-off card or a saved card. Does not take the money.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = PaymentMappers.BuyerId(user);
                return await HandleAsync(request, service, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService service, CancellationToken ct)
    {
        if (request.Card is null && !request.SavedPaymentMethodId.HasValue)
        {
            throw new PaymentValidationException("Provide either card details or a saved payment method id.");
        }
        if (request.Card is not null && request.SavedPaymentMethodId.HasValue)
        {
            throw new PaymentValidationException("Provide only one of card details or a saved payment method id.");
        }

        var instruction = request.SavedPaymentMethodId.HasValue
            ? new PaymentInstruction(null, request.SavedPaymentMethodId.Value)
            : new PaymentInstruction(PaymentMappers.ToCardDetails(request.Card!), null);

        var payment = await service.AuthorizeOrderAsync(request.OrderId, request.BuyerId, instruction, ct);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = payment.OrderId,
            Status = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            Amount = payment.Amount,
            Currency = payment.CurrencyCode
        });
    }
}
