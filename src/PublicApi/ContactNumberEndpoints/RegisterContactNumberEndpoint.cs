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

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates the number and returns
/// its canonical form, which is what gets stored. An unusable destination is rejected here.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service, ClaimsPrincipal user, HttpContext http) =>
            {
                request.CallerId = user.Identity?.Name;
                return await HandleAsync(request, service, http.RequestAborted);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service) =>
        HandleAsync(request, service, default);

    private static async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, System.Threading.CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CallerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterAsync(request.CallerId, request.Number, ct);
        if (!result.Registered)
        {
            return Results.BadRequest(new { message = result.Reason });
        }

        return Results.Created($"api/contact-numbers/{result.ContactNumberId}", new RegisterContactNumberResponse
        {
            ContactNumberId = result.ContactNumberId!.Value
        });
    }
}
