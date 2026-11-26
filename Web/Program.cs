using Web.Configuration;

DotEnvConfiguration.LoadFromRepositoryRoot();
DotEnvConfiguration.ConfigureAspNetCoreUrls();

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureApplicationServices();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Alaasmagi Dotnet Template API v1");
    });
}

app.UseAuthentication();
app.UseAuthorization();

// After authorization, which authenticates API calls with the bearer scheme, so the limiter can partition by sub.
app.UseRateLimiter();

app.MapStaticAssets();
app.MapHealthChecks("/health").AllowAnonymous();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
