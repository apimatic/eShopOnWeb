using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest>
{
    private readonly IOrderNotificationService _service;

    public ListContactNumbersEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new ListContactNumbersRequest { CallerId = user.GetUserId() }, ct);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(ListContactNumbersRequest request) => HandleAsync(request, default);

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, CancellationToken ct)
    {
        var response = new ListContactNumbersResponse(request.CorrelationId());
        if (string.IsNullOrEmpty(request.CallerId)) return Results.Unauthorized();

        var numbers = await _service.GetContactNumbersAsync(request.CallerId, ct);
        response.ContactNumbers = numbers.Select(ContactNumberDto.From).ToList();
        return Results.Ok(response);
    }
}
