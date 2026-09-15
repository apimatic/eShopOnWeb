using System.Security.Claims;
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

/// <summary>
/// Authorizes (holds) the order total against a card — one-off card details or one of the shopper's saved
/// cards. Does not take the money; that happens at fulfilment. Idempotent: a double-click never holds twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentOrchestrationService service, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity!.Name!;
                return await ExecuteAsync(request, service, ct);
            })
            .Produces<OrderPaymentView>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(PayOrderRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var command = new PayCommand(request.PaymentMethodId, ToCardCommand(request.Card));
        var result = await service.AuthorizeAsync(request.BuyerId, request.OrderId, command, ct);
        return result.ToHttpResult(Results.Ok);
    }

    private static CardCommand? ToCardCommand(CardDto? card) =>
        card is null
            ? null
            : new CardCommand(card.Name, card.Number, card.Expiry, card.SecurityCode,
                card.BillingAddress is null
                    ? null
                    : new BillingAddressCommand(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
                        card.BillingAddress.AdminArea1, card.BillingAddress.AdminArea2, card.BillingAddress.PostalCode,
                        card.BillingAddress.CountryCode));
}
