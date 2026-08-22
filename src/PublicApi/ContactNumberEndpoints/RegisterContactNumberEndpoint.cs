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

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RegisterContactNumberRequest request, IContactNumberService service, HttpContext http, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(http.User);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, service, buyerId, ct);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, string.Empty, CancellationToken.None);

    private static async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, string buyerId, CancellationToken ct)
    {
        var result = await service.RegisterAsync(buyerId, request.PhoneNumber, ct);
        return ResultHttp.ToHttp(result, entity =>
        {
            var response = new RegisterContactNumberResponse
            {
                ContactNumberId = entity.Id,
                PhoneNumber = entity.CanonicalNumber
            };
            return Results.Created($"api/contact-numbers/{entity.Id}", response);
        });
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
