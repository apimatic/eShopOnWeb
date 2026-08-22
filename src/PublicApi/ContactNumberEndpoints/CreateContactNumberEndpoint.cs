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

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IShopperContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, IShopperContactNumberService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, service, cancellationToken);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IShopperContactNumberService service)
        => HandleAsync(request, new ClaimsPrincipal(), service, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateContactNumberRequest request,
        ClaimsPrincipal user,
        IShopperContactNumberService service,
        CancellationToken cancellationToken)
    {
        var created = await service.RegisterAsync(
            user.GetBuyerId(),
            request.PhoneNumber,
            request.CountryCode,
            cancellationToken);

        var response = new CreateContactNumberResponse
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.CanonicalNumber
        };
        return Results.Created($"api/contact-numbers/{created.Id}", response);
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public class CreateContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
