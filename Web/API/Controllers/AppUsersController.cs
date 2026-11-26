using Asp.Versioning;
using Base.Authorization;
using Base.Contracts.Authorization;
using Base.Contracts.DTO;
using Base.DTO;
using Contracts.Application;
using DTO.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Web.Configuration;
using IMapper = Base.Contracts.DTO.IMapper<DTO.Web.AppUserDto, Domain.AppUser>;

namespace Web.API.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[EnableRateLimiting(RateLimitPolicies.Api)]
[Route("api/v{version:apiVersion}/app-users")]
[Produces("application/json")]
public class AppUsersController(
    IAppUserService appUserService,
    IMapper mapper,
    ICurrentUser currentUser) : ControllerBase
{
    private const string Module = "app-users";

    [HttpGet]
    [RequirePermission(Module, Actions.ViewAll)]
    public async Task<ActionResult<IEnumerable<AppUserDto>>> GetAll()
    {
        var result = await appUserService.GetAllAsync();
        if (!result.Successful)
        {
            return ToProblem(result.Error);
        }

        return Ok(mapper.Map(result.Value)?.ToList() ?? []);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppUserDto>> GetById(Guid id)
    {
        if (id != currentUser.Id && !currentUser.HasPermission(Module, Actions.ViewAll))
        {
            return Forbid(JwtBearerDefaults.AuthenticationScheme);
        }

        var result = await appUserService.GetByIdAsync(id);
        if (!result.Successful)
        {
            return ToProblem(result.Error);
        }

        return Ok(mapper.Map(result.Value));
    }

    private ObjectResult ToProblem(IError? error)
    {
        var statusCode = error?.Code switch
        {
            ErrorDefaults.Codes.NotFound => StatusCodes.Status404NotFound,
            ErrorDefaults.Codes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorDefaults.Codes.ConcurrencyConflict => StatusCodes.Status409Conflict,
            ErrorDefaults.Codes.ConcurrencyTokenRequired => StatusCodes.Status428PreconditionRequired,
            ErrorDefaults.Codes.InvalidPaging => StatusCodes.Status400BadRequest,
            ErrorDefaults.Codes.MapFailed => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(
            statusCode: statusCode,
            title: error?.Code ?? "REQUEST_FAILED",
            detail: error?.Message ?? "The request failed.");
    }
}
