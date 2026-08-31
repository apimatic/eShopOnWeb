using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest : BaseRequest
{
    /// <summary>Populated from the JWT, never from the request.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new List<ContactNumberDto>();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Lists the signed-in shopper's registered numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IReadRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(new ListContactNumbersRequest { BuyerId = user.Identity!.Name! }, contactNumberRepository);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IReadRepository<ContactNumber> contactNumberRepository)
    {
        var numbers = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(request.BuyerId));

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber,
                CreatedAt = n.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
