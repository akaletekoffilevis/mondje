using Mondje.Infrastructure.Data;
using Mondje.Web.Api.Infrastructure;
using Mondje.Web.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Domain / application / infrastructure (MediatR, EF Core, import engine).
builder.AddApplicationServices();
builder.AddInfrastructureServices();

// API services (endpoints, exception handling, OpenAPI, import queue and worker).
builder.AddWebServices();

// MVC front end.
builder.Services.AddControllersWithViews();

// Internal calls from the MVC controllers go back to the same public host.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<SameHostRequestHandler>();
builder.Services.AddHttpClient<MondjeApiClient>(client =>
    {
        client.BaseAddress = new Uri("http://same-host");
        client.Timeout = TimeSpan.FromMinutes(2);
    })
    .AddHttpMessageHandler<SameHostRequestHandler>();

var app = builder.Build();

// Apply migrations in every environment (including Production). A failure must not stop
// the site from starting: hosting logs stay available and the error is visible in the UI.
try
{
    await app.InitialiseDatabaseAsync();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Database initialisation failed; the application will keep running.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Browser API reference: /scalar
    app.MapScalarApiReference();
}
else
{
    // The default HSTS value is 30 days. See https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseExceptionHandler("/Home/Error");
}

// API errors are always problem+json, including in Production.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseExceptionHandler());

app.UseHttpsRedirection();
app.UseCors(static policy =>
    policy.AllowAnyMethod()
        .AllowAnyHeader()
        .AllowAnyOrigin());

// Every request carries an identifier in the logs (and in X-Request-Id).
app.UseMiddleware<RequestIdMiddleware>();

app.UseStaticFiles();
app.UseRouting();

app.MapEndpoints(typeof(Program).Assembly);
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program;
