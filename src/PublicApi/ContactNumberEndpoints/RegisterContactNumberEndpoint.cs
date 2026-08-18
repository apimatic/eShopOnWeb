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
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. The number is
/// validated and canonicalized with the provider first; an unusable number is rejected here, and the
/// provider's canonical form is what is stored. Returns the new contactNumberId.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        var ownerId = EndpointCaller.UserName(_httpContextAccessor);
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterAsync(ownerId, request.PhoneNumber, EndpointCaller.RequestAborted(_httpContextAccessor));
        if (!result.Succeeded || result.ContactNumber is null)
        {
            return Results.BadRequest(new { error = result.Error ?? "The phone number could not be registered." });
        }

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = result.ContactNumber.Id,
            E164Number = result.ContactNumber.E164Number
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
