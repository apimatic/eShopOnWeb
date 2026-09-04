using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total with PayPal (hold only — money is taken at fulfilment).
/// Double-calling is idempotent: a second call replays the existing hold.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public PayOrderEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(orderId, request, user);
            })
            .Produces<PayOrderResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = AuthenticatedUser.RequireIdentity(user);

        var command = new PayCommand(
            Card: request.Card?.ToCredential(),
            SavedPaymentMethodId: request.PaymentMethodId,
            ExpectedAmount: request.ExpectedAmount);

        var result = await _payments.PayAsync(orderId, buyerId, command);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString(),
            Replayed = result.Replayed,
            Payment = PaymentDtos.PaymentDto.From(result.Order)
        };

        return Results.Ok(response);
    }
}
