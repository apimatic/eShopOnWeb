using System;
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
    public string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public System.Collections.Generic.List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<Buyer>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<Buyer> buyerRepository) =>
            {
                var request = new ListPaymentMethodsRequest { BuyerId = user.Identity!.Name! };
                return await HandleAsync(request, buyerRepository);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<Buyer> buyerRepository)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());

        var buyer = await buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpec(request.BuyerId));
        if (buyer is not null)
        {
            response.PaymentMethods = buyer.PaymentMethods.Select(PaymentMethodDto.FromEntity).ToList();
        }

        return Results.Ok(response);
    }
}
