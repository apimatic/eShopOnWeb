using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card and
/// describes it safely (brand, last 4, expiry) — never full card details. Returns <c>paymentMethodId</c>.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service, CancellationToken ct)
    {
        var paymentMethod = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails(), request.Alias, ct);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethod.Id,
            PaymentMethod = paymentMethod.ToDto()
        };
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
