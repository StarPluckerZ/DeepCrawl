using DeepCrawl.Core;
using DeepCrawl.Infrastructure;
using DeepCrawl.Infrastructure.Auth;
using DeepCrawl.Infrastructure.Plugins;
using Microsoft.AspNetCore.Authentication;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

var mvc = builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddAuthentication("ApiToken")
    .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>("ApiToken", null);
builder.Services.AddAuthorization();

builder.Services.AddDeepCrawlInfra(builder.Configuration);
builder.Services.AddDeepCrawlCore(builder.Configuration);

PluginFinder.LoadPlugins(builder.Configuration["Plugins:Path"],
    builder.Environment.ContentRootPath, builder.Services, builder.Configuration,
    mvc.PartManager);

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
