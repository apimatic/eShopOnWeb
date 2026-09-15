using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the numbers the signed-in shopper has registered. Only ever the caller's own.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListContactNumbersResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var repository = httpContext.RequestServices.GetRequiredService<IRepository<ContactNumber>>();
        var numbers = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), httpContext.RequestAborted);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .OrderBy(n => n.Id)
                .Select(n => new ContactNumberDto
                {
                    ContactNumberId = n.Id,
                    PhoneNumber = n.PhoneNumber,
                    RegisteredAt = n.RegisteredAt
                })
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}
