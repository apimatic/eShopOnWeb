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
/// Registers a mobile number for the signed-in shopper after Twilio Lookup validates it.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, IContactNumberService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
    {
        var buyerId = _httpContextAccessor.HttpContext?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var created = await service.RegisterAsync(buyerId, request.PhoneNumber);
        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}
