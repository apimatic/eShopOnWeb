using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>GET /api/contact-numbers — the signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IReadRepository<ContactNumber> repository,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrWhiteSpace(buyerId))
                    return Results.Unauthorized();

                var numbers = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
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
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }
}
