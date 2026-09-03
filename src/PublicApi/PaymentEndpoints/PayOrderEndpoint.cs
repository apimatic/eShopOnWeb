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

public class PayOrderRequest
{
    public int OrderId { get; set; }
    /// <summary>Card details for a one-off payment. Provide this OR <see cref="SavedCardId"/>.</summary>
    public CardDetailsDto? Card { get; set; }
    /// <summary>Id of one of the caller's saved cards. Provide this OR <see cref="Card"/>.</summary>
    public int? SavedCardId { get; set; }
}

/// <summary>
/// Authorizes (places a hold for) the order total. Does not capture. Idempotent in effect.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public PayOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IPaymentService service) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentView>()
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var view = await service.PayAsync(
            ctx.User.BuyerId(), request.OrderId,
            new PayInstruction(request.Card.ToCardInput(), request.SavedCardId),
            ctx.RequestAborted);
        return Results.Ok(view);
    }
}
