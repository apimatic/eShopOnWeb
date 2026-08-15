using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    /// <summary>Raw card for a one-off payment. Omit when paying with a saved card.</summary>
    public CardRequestDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards. Omit when paying with raw card details.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = default!;

    [JsonIgnore]
    public int OrderId { get; set; }
}

public class PayOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = default!;
    public PaymentStateDto? Payment { get; set; }
}

/// <summary>Authorizes (holds) the order total. Shopper-scoped, idempotent in effect.</summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.BuyerId = ApiCaller.BuyerId(user);
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var instruction = new PayInstruction
        {
            Card = request.Card?.ToCardDetails(),
            SavedPaymentMethodId = request.SavedPaymentMethodId
        };

        var order = await service.AuthorizeAsync(request.BuyerId, request.OrderId, instruction);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Payment = PaymentStateDto.From(order.Payment)
        });
    }
}
