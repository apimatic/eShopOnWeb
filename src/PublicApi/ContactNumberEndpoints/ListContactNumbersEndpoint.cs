using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(new ListContactNumbersRequest(httpContext.User.Identity?.Name), service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService service)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListForBuyerAsync(request.BuyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId());
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.CanonicalNumber
        }));

        return Results.Ok(response);
    }
}

public class ListContactNumbersRequest : BaseRequest
{
    public ListContactNumbersRequest(string? buyerId)
    {
        BuyerId = buyerId;
    }

    public string? BuyerId { get; }
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
