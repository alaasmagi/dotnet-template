using System.Reflection;
using Base.Authorization;
using Base.Contracts.Authorization;
using Base.Contracts.DTO;
using Base.DTO;
using Contracts.Application;
using Domain;
using DTO.Web;
using DTO.Web.Mappers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.API.Controllers;

namespace Tests;

public class AppUsersControllerTests
{
    private readonly Guid _callerId = Guid.NewGuid();
    private FakeCurrentUser _currentUser = default!;
    private AppUsersController _controller = default!;

    [SetUp]
    public void SetUp()
    {
        _currentUser = new FakeCurrentUser(_callerId);
        _controller = new AppUsersController(new FakeAppUserService(), new AppUserDtoMapper(), _currentUser);
    }

    [Test]
    public async Task GetById_OwnId_WithoutPermission_ReturnsUser()
    {
        var result = await _controller.GetById(_callerId);

        Assert.That((result.Result as OkObjectResult)?.Value, Is.TypeOf<AppUserDto>().With.Property(nameof(AppUserDto.Id)).EqualTo(_callerId));
    }

    [Test]
    public async Task GetById_OtherId_WithoutPermission_ForbidsWithBearerScheme()
    {
        var result = await _controller.GetById(Guid.NewGuid());

        Assert.That(result.Result, Is.TypeOf<ForbidResult>());
        Assert.That(((ForbidResult)result.Result!).AuthenticationSchemes, Is.EqualTo(new[] { JwtBearerDefaults.AuthenticationScheme }));
    }

    [Test]
    public async Task GetById_OtherId_WithViewAllPermission_ReturnsUser()
    {
        _currentUser.Granted.Add("app-users.view-all");
        var otherId = Guid.NewGuid();

        var result = await _controller.GetById(otherId);

        Assert.That((result.Result as OkObjectResult)?.Value, Is.TypeOf<AppUserDto>().With.Property(nameof(AppUserDto.Id)).EqualTo(otherId));
    }

    [Test]
    public void GetAll_RequiresViewAllPermission()
    {
        var attribute = typeof(AppUsersController).GetMethod(nameof(AppUsersController.GetAll))!
            .GetCustomAttribute<RequirePermissionAttribute>();

        Assert.That(attribute?.Policy, Is.EqualTo(PermissionPolicyNames.For("app-users.view-all")));
    }

    [Test]
    public void Controller_AuthenticatesWithBearerSchemeOnly()
    {
        var attribute = typeof(AppUsersController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(attribute?.AuthenticationSchemes, Is.EqualTo(JwtBearerDefaults.AuthenticationScheme));
    }

    [Test]
    public void Controller_HasNoWriteEndpoints()
    {
        var actions = typeof(AppUsersController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);

        Assert.That(actions, Is.EquivalentTo(new[] { nameof(AppUsersController.GetAll), nameof(AppUsersController.GetById) }));
    }

    private class FakeCurrentUser(Guid id) : ICurrentUser
    {
        public HashSet<string> Granted { get; } = [];

        public bool IsAuthenticated => true;
        public Guid Id => id;
        public string? Email => "user@example.com";
        public string? Name => "Full Name";
        public bool IsService => false;
        public string? ClientId => null;
        public IReadOnlySet<string> Permissions => Granted;

        public bool HasPermission(string module, string action) => Granted.Contains($"{module}.{action}");
    }

    private class FakeAppUserService : IAppUserService
    {
        public Task<IMethodResponse<AppUser>> GetByIdAsync(Guid id, Guid actor = default)
        {
            return Task.FromResult<IMethodResponse<AppUser>>(MethodResponse<AppUser>.Success(
                AppUser.FromIdentity(id, "user@example.com", "user", "Full Name", null, null)));
        }

        public Task<IMethodResponse<bool>> SyncFromIdentityAsync(Guid messageId, AppUser appUser) => throw new NotSupportedException();

        public Task<IMethodResponse<bool>> RemoveFromIdentityAsync(Guid messageId, Guid id) => throw new NotSupportedException();

        public Task<IMethodResponse<IEnumerable<AppUser>>> GetAllAsync(Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<IEnumerable<AppUser>>> GetAllByPageAsync(int pageNr, int pageSize, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<int>> GetCountAsync(Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<bool>> ExistsAsync(Guid id, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<AppUser>> CreateAsync(AppUser entity, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<AppUser>> UpdateAsync(Guid id, AppUser entity, string? expectedConcurrencyToken = null, Guid actor = default) => throw new NotSupportedException();

        public Task<IMethodResponse<bool>> RemoveAsync(Guid id, string? expectedConcurrencyToken = null, Guid actor = default) => throw new NotSupportedException();
    }
}
