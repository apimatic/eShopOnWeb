using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RegisterContactNumberRequest request, IContactNumberService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var buyerId = http.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var created = await service.RegisterAsync(buyerId, request.PhoneNumber, http.RequestAborted);
            var response = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = created.Id,
                PhoneNumber = created.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (UnusableDestinationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (DuplicateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (ProviderUnavailableException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
