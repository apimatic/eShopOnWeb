using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards (safe display data only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly ISavedCardService _savedCardService;

    public ListPaymentMethodsEndpoint(ISavedCardService savedCardService)
    {
        _savedCardService = savedCardService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var savedCards = await _savedCardService.ListAsync(buyerId);

        var response = new ListPaymentMethodsResponse();
        foreach (var card in savedCards)
        {
            response.PaymentMethods.Add(new PaymentMethodDto
            {
                PaymentMethodId = card.Id,
                Brand = card.Brand,
                LastDigits = card.LastDigits,
                Expiry = card.Expiry,
                CardholderName = card.CardholderName,
                CreatedAt = card.CreatedAt
            });
        }
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
