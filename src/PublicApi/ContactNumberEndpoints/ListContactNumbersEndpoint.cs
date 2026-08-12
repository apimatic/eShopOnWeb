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

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}

public class ListContactNumbersRequest : BaseRequest
{
    internal string BuyerId { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) { }
    public ListContactNumbersResponse() { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Returns the signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IContactNumberService service) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new ListContactNumbersRequest { BuyerId = buyerId }, service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService service)
    {
        var numbers = await service.ListAsync(request.BuyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId())
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber,
                CreatedDate = n.CreatedDate
            }).ToList()
        };
        return Results.Ok(response);
    }
}
