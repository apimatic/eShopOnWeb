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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, ICheckoutService checkout, CancellationToken ct) =>
            {
                var card = request.Card is null
                    ? null
                    : new CardPaymentDetails(
                        request.Card.Number,
                        request.Card.Expiry,
                        request.Card.SecurityCode,
                        request.Card.Name,
                        request.Card.BillingAddress is null
                            ? null
                            : new CardBillingAddress(
                                request.Card.BillingAddress.CountryCode,
                                request.Card.BillingAddress.AddressLine1,
                                request.Card.BillingAddress.AdminArea1,
                                request.Card.BillingAddress.AdminArea2,
                                request.Card.BillingAddress.PostalCode));

                var result = await checkout.PayAsync(
                    CallerIdentity.BuyerId(user),
                    orderId,
                    card,
                    request.PaymentMethodId,
                    ct);

                return Results.Ok(new PayOrderResponse
                {
                    OrderId = result.OrderId,
                    Status = result.Status.ToString(),
                    PayPalOrderId = result.PayPalOrderId,
                    AuthorizationId = result.AuthorizationId,
                    AuthorizationStatus = result.AuthorizationStatus,
                    AuthorizationExpiration = result.AuthorizationExpiration,
                    Amount = result.Amount,
                    Currency = result.Currency
                });
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}
