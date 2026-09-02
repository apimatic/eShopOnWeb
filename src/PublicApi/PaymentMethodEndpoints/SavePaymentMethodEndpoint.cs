using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Vaults a card for the signed-in shopper. The response identifies the saved card and
/// describes it only by safe display data — full card details are never stored or returned.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly OrderPaymentService _paymentService;

    public SavePaymentMethodEndpoint(OrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await Handle(request, user, ct);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user)
        => Handle(request, user, CancellationToken.None);

    private async Task<IResult> Handle(SavePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            var buyerId = user.Identity?.Name;
            if (buyerId is null)
            {
                return Results.Unauthorized();
            }

            var saved = await _paymentService.SaveCardAsync(buyerId, new GatewayCard
            {
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.Cvc,
                Name = request.Card.Name,
                AddressLine1 = request.Card.AddressLine1,
                City = request.Card.City,
                State = request.Card.State,
                PostalCode = request.Card.PostalCode,
                CountryCode = request.Card.CountryCode
            }, ct);

            return Results.Created($"api/payment-methods/{saved.Id}", new SavePaymentMethodResponse
            {
                PaymentMethodId = saved.Id,
                Brand = saved.Brand,
                LastDigits = saved.LastDigits,
                Expiry = saved.Expiry
            });
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or PaymentGatewayException)
        {
            return ApiErrorResults.FromException(ex);
        }
    }
}
