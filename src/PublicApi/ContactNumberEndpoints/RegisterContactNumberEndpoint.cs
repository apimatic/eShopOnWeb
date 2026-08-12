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

/// <summary>
/// POST /api/contact-numbers — register a mobile number for the signed-in shopper. The number is
/// validated with the provider; an unusable destination is rejected here, and the provider's canonical
/// form is what gets stored.
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
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var buyerId = http.User.Identity!.Name!;

        var view = await service.RegisterAsync(buyerId, request.PhoneNumber, http.RequestAborted);

        return Results.Created($"api/contact-numbers/{view.ContactNumberId}", new RegisterContactNumberResponse
        {
            ContactNumberId = view.ContactNumberId,
            PhoneNumber = view.PhoneNumber,
            CreatedAt = view.CreatedAt
        });
    }
}
