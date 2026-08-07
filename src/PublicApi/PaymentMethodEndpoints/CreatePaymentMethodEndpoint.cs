using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper. Full card details are never stored or returned.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentMethodService _paymentMethodService;

    public CreatePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, ct);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var paymentMethod = await _paymentMethodService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), ct);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethod.Id,
            PaymentMethod = PaymentMethodDto.FromEntity(paymentMethod)
        };
        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
