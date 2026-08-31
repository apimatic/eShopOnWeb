using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards (safe display data only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedCardService savedCardService) =>
            {
                return await HandleAsync(savedCardService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var cards = await savedCardService.ListAsync(buyerId);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(PaymentMethodDto.FromSavedCard).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new List<PaymentMethodDto>();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static PaymentMethodDto FromSavedCard(SavedCard card) => new PaymentMethodDto
    {
        PaymentMethodId = card.Id,
        LastDigits = card.LastDigits,
        Brand = card.Brand,
        Expiry = card.Expiry,
        CreatedAt = card.CreatedAt
    };
}
