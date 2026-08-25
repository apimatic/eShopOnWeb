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

public class ListPaymentMethodsRequest : BaseRequest
{
    public ListPaymentMethodsRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? Alias { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's own saved cards. Never returns full card details.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<Buyer>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Buyer> buyerRepository) =>
            {
                var request = new ListPaymentMethodsRequest(user.Identity!.Name!);
                return await HandleAsync(request, buyerRepository);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<Buyer> buyerRepository)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var buyer = await buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(request.BuyerId));

        response.PaymentMethods = buyer?.PaymentMethods.Select(p => new PaymentMethodDto
        {
            PaymentMethodId = p.Id,
            Brand = p.Brand,
            Last4 = p.Last4,
            Expiry = p.Expiry,
            Alias = p.Alias
        }).ToList() ?? new List<PaymentMethodDto>();

        return Results.Ok(response);
    }
}
