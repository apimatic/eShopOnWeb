using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? DisplayFormat { get; set; }
    public string? CountryCode { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Returns the caller's own registered numbers, and only theirs.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                IRepository<ContactNumber> repository,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var numbers = await repository.ListAsync(
                    new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);

                var response = new ListContactNumbersResponse
                {
                    ContactNumbers = numbers.Select(c => new ContactNumberDto
                    {
                        ContactNumberId = c.Id,
                        PhoneNumber = c.PhoneNumber,
                        DisplayFormat = c.DisplayFormat,
                        CountryCode = c.CountryCode,
                        RegisteredAt = c.RegisteredAt
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }
}
