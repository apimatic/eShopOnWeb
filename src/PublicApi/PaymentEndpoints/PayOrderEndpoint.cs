using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedPaymentMethodId"/>.</summary>
    public CardRequestModel? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? SavedPaymentMethodId { get; set; }

    [JsonIgnore] public int OrderId { get; set; }
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
    [JsonIgnore] public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total for the shopper's own order.
/// Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service, HttpContext ctx) =>
            {
                request.OrderId = orderId;
                request.BuyerId = CallerIdentity.BuyerId(user);
                request.CancellationToken = ctx.RequestAborted;
                return await HandleAsync(request, service);
            })
            .Produces<OrderDto>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
    {
        var instruction = new PayInstruction(request.Card?.ToDomain(), request.SavedPaymentMethodId);
        var order = await service.PayAsync(request.OrderId, request.BuyerId, instruction, request.CancellationToken);
        return Results.Ok(PaymentMappings.ToDto(order));
    }
}
