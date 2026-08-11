using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodRequest
{
    public CardRequest Card { get; set; } = default!;
}

public class SavePaymentMethodResponse
{
    /// <summary>The saved card's id (top-level, as required).</summary>
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = default!;
    public string LastDigits { get; set; } = default!;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class ListPaymentMethodsResponse
{
    public IReadOnlyList<PaymentMethodView> PaymentMethods { get; set; } = new List<PaymentMethodView>();
}

/// <summary>Saves (vaults) a card for the signed-in shopper. The response describes the card safely — never full details.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                var buyerId = CurrentUser.BuyerId(user);
                if (request?.Card is null)
                {
                    throw new PaymentException("Card details are required to save a payment method.");
                }

                var method = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), ct);

                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = method.Id,
                    CardBrand = method.CardBrand,
                    LastDigits = method.LastDigits,
                    Expiry = method.ExpiryYearMonth,
                    CardholderName = method.CardholderName
                };
                return Results.Created($"api/payment-methods/{method.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Lists the caller's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                var buyerId = CurrentUser.BuyerId(user);
                var methods = await service.ListAsync(buyerId, ct);
                return Results.Ok(new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(PaymentMethodView.From).ToList()
                });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}

/// <summary>Removes one of the caller's saved cards; afterwards it can no longer be seen or used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                var buyerId = CurrentUser.BuyerId(user);
                await service.DeleteAsync(buyerId, paymentMethodId, ct);
                return Results.NoContent();
            })
            .WithTags("PaymentMethodEndpoints");
    }
}
