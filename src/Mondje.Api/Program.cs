using Mondje.Api.Infrastructure;
using Mondje.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.InitialiseDatabaseAsync();
    app.MapOpenApi();
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

app.UseExceptionHandler(options => { });

app.MapEndpoints(typeof(Program).Assembly);

app.Run();
