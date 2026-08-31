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

/// <summary>
/// Lists the signed-in shopper's registered mobile numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IOrderNotificationService _notificationService;

    public ListContactNumbersEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var response = new ListContactNumbersResponse();

        var contactNumbers = await _notificationService.GetContactNumbersAsync(
            httpContext.User.Identity!.Name!, httpContext.RequestAborted);
        response.ContactNumbers = contactNumbers.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.PhoneNumber,
            CreatedAt = c.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}
