using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeepCrawl.Domain.Abstractions;

public interface IPlugin
{
    void Configure(IServiceCollection services, IConfiguration configuration);
}
