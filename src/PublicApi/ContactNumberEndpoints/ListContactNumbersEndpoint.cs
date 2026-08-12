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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ContactNumberDto
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Returns the caller's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, ClaimsPrincipal user) =>
            {
                var request = new ListContactNumbersRequest { BuyerId = CallerIdentity.GetUserName(user) };
                return await HandleAsync(request, service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListAsync(request.BuyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId())
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto { Id = n.Id, PhoneNumber = n.PhoneNumber })
                .ToList()
        };
        return Results.Ok(response);
    }
}
