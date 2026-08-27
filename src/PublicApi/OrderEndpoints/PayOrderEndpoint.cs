using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order total with PayPal, using one-off card details
/// or one of the caller's saved cards. Does not take the money yet.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var payment = await paymentService.AuthorizePaymentAsync(
            request.BuyerId, request.OrderId, request.Card?.ToModel(), request.PaymentMethodId);

        response.OrderId = request.OrderId;
        response.Payment = PaymentDto.FromDomain(payment);
        return Results.Ok(response);
    }
}

public class PayOrderRequest : BaseRequest
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OrderId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>One-off card details. Mutually exclusive with PaymentMethodId.</summary>
    public CardRequest? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards. Mutually exclusive with Card.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
