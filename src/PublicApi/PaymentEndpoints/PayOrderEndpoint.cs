using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (places a hold for) the order total. The request carries either card details for a one-off
/// payment or the id of one of the shopper's saved cards. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.OrderId = orderId;
                request.CallerName = user.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.CallerName))
        {
            return Results.Unauthorized();
        }

        var instrument = new PaymentInstrument
        {
            SavedPaymentMethodId = request.SavedPaymentMethodId,
            Card = request.Card is null ? null : PaymentMappers.ToCardDetails(request.Card)
        };

        var payment = await service.AuthorizeAsync(request.CallerName, request.OrderId, instrument);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            Payment = PaymentMappers.ToDto(payment)
        };
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string? CallerName { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public OrderPaymentDto Payment { get; set; } = new();
}
