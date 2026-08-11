using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total. The request carries either card details for a
/// one-off payment, or the id of one of the shopper's saved cards. Money is not taken.
/// Idempotent in effect: a repeat while already held returns the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderCommand, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService) =>
                await HandleAsync(new PayOrderCommand(orderId, request), paymentService))
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderCommand command, IPaymentService paymentService)
    {
        var buyerId = CallerIdentity.BuyerId(_httpContextAccessor.HttpContext!);
        var body = command.Body ?? new PayOrderRequest(null, null);

        if (body.Card is null && body.SavedPaymentMethodId is null)
        {
            throw new PaymentException("Provide card details or the id of a saved card.", 400);
        }

        var instruction = new PaymentInstruction(
            body.Card is null ? null : PaymentRequestMapper.ToCardDetails(body.Card),
            body.SavedPaymentMethodId);

        var payment = await paymentService.AuthorizeAsync(buyerId, command.OrderId, instruction);

        return Results.Ok(new OrderPaymentResponse(command.OrderId, PaymentMapper.ToDto(payment)!));
    }
}
