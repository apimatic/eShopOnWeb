using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Body of POST /api/orders/{orderId}/pay: either card details or a saved card id.</summary>
public record PayOrderRequest(CardRequest? Card, int? SavedCardId, bool SaveCard = false);

public record PayOrderCommand(int OrderId, PayOrderRequest? Body);

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. The request carries card
/// details for a one-off payment, or names one of the shopper's saved cards. Shopper-scoped: only the
/// order's owner may pay it. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _settings;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest? body, IPaymentService paymentService) =>
                await HandleAsync(new PayOrderCommand(orderId, body), paymentService))
            .Produces<OrderPaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderCommand command, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        var body = command.Body
            ?? throw new PaymentException("Provide either card details or a saved card id to pay with.");

        var instruction = new PaymentInstruction(
            body.Card is null ? null : PaymentDtoMapper.ToCardDetails(body.Card),
            body.SavedCardId,
            body.SaveCard);

        var order = await paymentService.AuthorizeAsync(buyerId, command.OrderId, instruction);
        return Results.Ok(PaymentDtoMapper.ToDto(order, _settings.Currency));
    }
}
