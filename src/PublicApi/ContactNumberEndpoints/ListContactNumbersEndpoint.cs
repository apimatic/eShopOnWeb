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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(new ListContactNumbersRequest { BuyerId = httpContext.GetBuyerId() }, contactNumberService);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService contactNumberService)
    {
        var numbers = await contactNumberService.ListForBuyerAsync(request.BuyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId())
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber,
                NationalFormat = n.NationalFormat,
                CreatedAt = n.CreatedAt
            }).ToList()
        };

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

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? NationalFormat { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
