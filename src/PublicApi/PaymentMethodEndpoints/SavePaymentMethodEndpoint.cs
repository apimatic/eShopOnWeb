using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies
/// the saved card and carries only safe display attributes — never full card
/// details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SavePaymentMethodEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, CancellationToken ct) =>
            {
                return await HandleAsync(request, ct);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request) => HandleAsync(request, CancellationToken.None);

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, CancellationToken ct)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.Expiry))
        {
            return Results.BadRequest(new { message = "Card number and expiry (YYYY-MM) are required." });
        }

        try
        {
            var savedCard = await _paymentService.SaveCardAsync(buyerId, request.ToCardDetails(), ct);

            var response = new SavePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = savedCard.Id,
                Brand = savedCard.Brand,
                LastDigits = savedCard.LastDigits,
                Expiry = savedCard.Expiry,
                CardholderName = savedCard.CardholderName
            };
            return Results.Created($"api/payment-methods/{savedCard.Id}", response);
        }
        catch (PaymentGatewayException ex)
        {
            return PaymentErrorMapper.ToErrorResult(ex);
        }
    }
}
