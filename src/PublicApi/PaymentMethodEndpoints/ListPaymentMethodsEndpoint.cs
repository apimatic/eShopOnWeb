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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<SavedCard>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IRepository<SavedCard> cardRepo) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = user.Identity?.Name ?? "" }, cardRepo);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<SavedCard> cardRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new ActiveSavedCardsByBuyerSpec(request.BuyerId);
        var cards = await cardRepo.ListAsync(spec);

        var response = new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = cards.Select(c => new PaymentMethodDto
            {
                PaymentMethodId = c.Id,
                Last4 = c.Last4,
                Brand = c.Brand,
                Expiry = c.Expiry,
                CardholderName = c.CardholderName
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string BuyerId { get; set; } = "";
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
