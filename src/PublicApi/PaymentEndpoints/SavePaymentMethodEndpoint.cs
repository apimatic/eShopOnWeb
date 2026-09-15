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
/// Saves a card for the signed-in shopper (vaulted with PayPal). The response describes the card safely
/// (brand, last four, expiry) — never full card details — and returns its identifier as a top-level
/// <c>paymentMethodId</c>.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentOrchestrationService service, CancellationToken ct) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await ExecuteAsync(request, service, ct);
            })
            .Produces<SavedCardView>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(SavePaymentMethodRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var card = new CardCommand(request.Name, request.Number, request.Expiry, request.SecurityCode,
            request.BillingAddress is null
                ? null
                : new BillingAddressCommand(request.BillingAddress.AddressLine1, request.BillingAddress.AddressLine2,
                    request.BillingAddress.AdminArea1, request.BillingAddress.AdminArea2, request.BillingAddress.PostalCode,
                    request.BillingAddress.CountryCode));

        var result = await service.SaveCardAsync(request.BuyerId, card, ct);
        return result.ToHttpResult(saved => Results.Created($"api/payment-methods/{saved.PaymentMethodId}", saved));
    }
}
