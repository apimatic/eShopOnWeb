using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest>
{
    private readonly IReadRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly ICurrentUser _currentUser;

    public ListPaymentMethodsEndpoint(IReadRepository<SavedPaymentMethod> savedPaymentMethodRepository, ICurrentUser currentUser)
    {
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _currentUser = currentUser;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            () =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest());
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var methods = await _savedPaymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(_currentUser.BuyerId));
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
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) {}
    public ListPaymentMethodsResponse() {}

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
