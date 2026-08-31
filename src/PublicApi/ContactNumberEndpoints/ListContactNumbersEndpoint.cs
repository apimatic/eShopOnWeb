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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Lists the signed-in shopper's registered contact numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(new ListContactNumbersRequest { BuyerId = user.Identity!.Name! }, contactNumberRepository);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IRepository<ContactNumber> contactNumberRepository)
    {
        var response = new ListContactNumbersResponse(request.CorrelationId());

        var numbers = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(request.BuyerId));
        response.ContactNumbers = numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.PhoneNumber,
            NationalFormat = n.NationalFormat,
            CreatedAt = n.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListContactNumbersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalFormat { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
