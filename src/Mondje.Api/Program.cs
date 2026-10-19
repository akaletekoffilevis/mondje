using Mondje.Api.Infrastructure;
using Mondje.Infrastructure.Data;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.InitialiseDatabaseAsync();
    app.MapOpenApi();

    // Browser API reference (Swagger-like): browse and try endpoints at http://localhost:5002/scalar
    app.MapScalarApiReference();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors(static policy =>
    policy.AllowAnyMethod()
        .AllowAnyHeader()
        .AllowAnyOrigin());

app.UseExceptionHandler();

// Rule: every request carries an identifier in the logs (and in X-Request-Id).
app.UseMiddleware<RequestIdMiddleware>();

app.MapEndpoints(typeof(Program).Assembly);

app.Run();
