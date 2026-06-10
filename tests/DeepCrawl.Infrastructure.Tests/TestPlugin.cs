using DeepCrawl.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeepCrawl.Infrastructure.Tests;

public class TestPlugin : IPlugin
{
    public bool Configured { get; private set; }

    public void Configure(IServiceCollection services, IConfiguration configuration)
    {
        Configured = true;
    }
}
