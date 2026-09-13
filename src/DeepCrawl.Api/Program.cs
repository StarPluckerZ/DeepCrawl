using DeepCrawl.Core;
using DeepCrawl.Infrastructure;
using DeepCrawl.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddAuthentication("ApiToken")
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>("ApiToken", null);
builder.Services.AddAuthorization();

builder.Services.AddDeepCrawlInfra(builder.Configuration);
builder.Services.AddDeepCrawlCore(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
