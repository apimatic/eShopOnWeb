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
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly IReadRepository<SavedCard> _savedCardRepository;

    public ListPaymentMethodsEndpoint(IReadRepository<SavedCard> savedCardRepository)
    {
        _savedCardRepository = savedCardRepository;
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
        var buyerId = user.Identity?.Name ?? string.Empty;
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId));

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(c => new PaymentMethodDto
            {
                PaymentMethodId = c.Id,
                Brand = c.Brand,
                Last4 = c.Last4,
                Expiry = c.Expiry,
                CardholderName = c.CardholderName,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
