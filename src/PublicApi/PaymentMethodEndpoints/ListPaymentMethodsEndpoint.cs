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

/// <summary>
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<SavedPaymentMethod> paymentMethodRepository) =>
            {
                return await HandleAsync(
                    new ListPaymentMethodsRequest { BuyerId = user.Identity?.Name ?? string.Empty },
                    paymentMethodRepository);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<SavedPaymentMethod> paymentMethodRepository)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var methods = await paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(request.BuyerId));
        response.PaymentMethods = methods.Select(m => new SavedPaymentMethodDto
        {
            PaymentMethodId = m.Id,
            Brand = m.Brand,
            LastDigits = m.LastDigits,
            Expiry = m.Expiry,
            CardholderName = m.CardholderName,
            CreatedAt = m.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<SavedPaymentMethodDto> PaymentMethods { get; set; } = new();
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
