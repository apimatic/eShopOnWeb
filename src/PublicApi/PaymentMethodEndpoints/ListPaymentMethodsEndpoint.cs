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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards (safe display data only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest>
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;

    public ListPaymentMethodsEndpoint(IRepository<SavedPaymentMethod> paymentMethodRepository)
    {
        _paymentMethodRepository = paymentMethodRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) => await HandleAsync(new ListPaymentMethodsRequest
            {
                BuyerId = user.Identity?.Name ?? string.Empty
            }))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request)
    {
        var savedCards = await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(request.BuyerId));

        return Results.Ok(new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = savedCards.Select(m => new PaymentMethodDto
            {
                PaymentMethodId = m.Id,
                Brand = m.Brand,
                LastDigits = m.LastDigits,
                Expiry = m.Expiry,
                CreatedAt = m.CreatedAt
            }).ToList()
        });
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
