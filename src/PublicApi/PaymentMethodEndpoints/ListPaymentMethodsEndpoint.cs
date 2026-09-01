using System;
using System.Collections.Generic;
using System.Linq;
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
/// Lists the caller's saved cards (safe-display fields only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public ListPaymentMethodsEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
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
        var buyerId = user.Identity!.Name!;
        var cards = await _orderPaymentService.ListSavedCardsAsync(buyerId);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(c => new SavedPaymentMethodDto
            {
                PaymentMethodId = c.Id,
                Brand = c.Brand,
                LastDigits = c.LastDigits,
                Expiry = c.Expiry,
                CardholderName = c.CardholderName,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new List<SavedPaymentMethodDto>();
}

public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
