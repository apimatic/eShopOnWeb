using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes the order total: places a hold on the money without taking it. The request
/// carries either one-off card details or the id of one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Authorize (hold) the order total", Tags = new[] { "Orders" })]
        async (int orderId, PayOrderRequest request, ClaimsPrincipal user,
               IOrderPaymentService service, IReadRepository<SavedPaymentMethod> savedCards, CancellationToken ct) =>
                await HandleAsync(orderId, request, user, service, savedCards, ct))
            .Produces<PayOrderResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, ClaimsPrincipal user,
        IOrderPaymentService service, IReadRepository<SavedPaymentMethod> savedCards, CancellationToken ct)
    {
        var buyerId = PaymentProblem.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        try
        {
            PaymentSourceInstruction source;
            if (request?.SavedPaymentMethodId is int methodId)
            {
                var card = await savedCards.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(methodId, buyerId), ct);
                if (card is null)
                    return Results.NotFound(new { message = "The requested saved card was not found for this shopper." });
                source = PaymentSourceInstruction.FromVault(card.PayPalVaultId);
            }
            else if (request?.Card is not null)
            {
                source = PaymentSourceInstruction.FromCard(request.Card.ToDomain());
            }
            else
            {
                return Results.BadRequest(new { message = "Provide either 'card' details or a 'savedPaymentMethodId' to pay with." });
            }

            var payment = await service.AuthorizeAsync(buyerId, orderId, source, ct);
            var response = new PayOrderResponse(
                orderId,
                payment.Status.ToString(),
                payment.AuthorizationId,
                payment.Amount,
                payment.CurrencyCode,
                payment.AuthorizationExpiresAt);

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
