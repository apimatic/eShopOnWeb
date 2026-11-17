using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, HttpContext http, PayOrderRequest request, PaymentService payments) =>
                {
                    request.OrderId = orderId;
                    request.BuyerId = http.User.Identity?.Name ?? string.Empty;
                    request.RequestAborted = http.RequestAborted;
                    return await HandleAsync(request, payments);
                })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, PaymentService payments)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var payment = await payments.PayAsync(
            request.BuyerId, request.OrderId, request.Card, request.PaymentMethodId, request.RequestAborted);

        return Results.Ok(new PayOrderResponse { Payment = payment });
    }
}

public class PayOrderRequest : PaymentRequestBase
{
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>One-off card details. Supply this or <see cref="PaymentMethodId"/>.</summary>
    public CardInput? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards.</summary>
    public string? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public OrderPaymentView Payment { get; set; } = null!;
}
