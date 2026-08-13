using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                return await HandleAsync(new ListContactNumbersRequest { BuyerId = buyerId }, repository);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IRepository<ContactNumber> repository)
    {
        var numbers = await repository.ListAsync(new ContactNumbersByBuyerSpecification(request.BuyerId!));

        var response = new ListContactNumbersResponse(request.CorrelationId());
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.PhoneNumber,
            RegisteredAt = n.RegisteredAt
        }));
        return Results.Ok(response);
    }
}

public class ListContactNumbersRequest : BaseRequest
{
    internal string? BuyerId { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(System.Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
