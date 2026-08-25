using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PayOrderDependencies(int OrderId, string BuyerId, IOrderPaymentService OrderPaymentService, IPaymentMethodService PaymentMethodService);

/// <summary>
/// Authorizes (holds, does not capture) an order's total, either with raw card details or a
/// previously saved card. Fulfil the order separately to actually take the money.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, PayOrderDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService, IPaymentMethodService paymentMethodService) =>
            {
                var deps = new PayOrderDependencies(orderId, user.Identity!.Name!, orderPaymentService, paymentMethodService);
                return await HandleAsync(request, deps);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, PayOrderDependencies deps)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var hasCard = !string.IsNullOrEmpty(request.CardNumber);
        var hasPaymentMethod = request.PaymentMethodId is not null;
        if (hasCard == hasPaymentMethod)
        {
            return Results.BadRequest("Specify exactly one of: raw card details (CardNumber, ...), or PaymentMethodId for a saved card.");
        }

        PaymentSourceRequest paymentSource;
        if (hasPaymentMethod)
        {
            var paymentMethod = await deps.PaymentMethodService.GetForBuyerAsync(deps.BuyerId, request.PaymentMethodId!.Value, default);
            if (paymentMethod is null)
            {
                return Results.BadRequest($"Payment method {request.PaymentMethodId} was not found.");
            }

            paymentSource = new PaymentSourceRequest(null, paymentMethod.PayPalVaultId);
        }
        else
        {
            if (string.IsNullOrEmpty(request.BillingAddressCountryCode))
            {
                return Results.BadRequest("BillingAddressCountryCode is required when paying with raw card details.");
            }

            var billingAddress = new PaymentAddress(
                request.BillingAddressLine1, request.BillingAddressCity, request.BillingAddressState,
                request.BillingAddressPostalCode, request.BillingAddressCountryCode);
            var card = new CardDetails(request.CardNumber!, request.CardExpiry!, request.CardSecurityCode!, request.CardholderName, billingAddress);
            paymentSource = new PaymentSourceRequest(card, null);
        }

        (Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order Order, AuthorizePaymentOutcome Outcome)? result;
        try
        {
            result = await deps.OrderPaymentService.PayAsync(deps.OrderId, deps.BuyerId, paymentSource, default);
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(ex.Message);
        }

        if (result is null)
        {
            return Results.NotFound();
        }

        response.OrderId = result.Value.Order.Id;

        switch (result.Value.Outcome)
        {
            case AuthorizePaymentAuthorized authorized:
                response.Status = "Authorized";
                response.AuthorizationId = authorized.AuthorizationId;
                response.AuthorizedAmount = authorized.AuthorizedAmount;
                response.Currency = authorized.Currency;
                break;
            case AuthorizePaymentRequiresAction requiresAction:
                response.Status = "RequiresAction";
                response.PayerActionUrl = requiresAction.PayerActionUrl;
                break;
        }

        return Results.Ok(response);
    }
}
