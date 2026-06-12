using System.Reflection;
using DeepCrawl.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeepCrawl.Infrastructure.Plugins;

public class PluginFinder
{
    public static void LoadPlugins(string? pluginPath, string contentRoot,
        IServiceCollection services, IConfiguration configuration, ApplicationPartManager partManager)
    {
        if (string.IsNullOrWhiteSpace(pluginPath)) return;

        var path = Path.IsPathRooted(pluginPath)
            ? pluginPath
            : Path.GetFullPath(Path.Combine(contentRoot, pluginPath));

        var finder = new PluginFinder();
        foreach (var plugin in finder.FindPlugins(path))
        {
            plugin.Configure(services, configuration);
            partManager.ApplicationParts.Add(new AssemblyPart(plugin.GetType().Assembly));
            Console.WriteLine($"[DeepCrawl] Loaded plugin: {plugin.GetType().Name}");
        }
    }

    public IEnumerable<IPlugin> FindPlugins(string pluginDir)
    {
        if (!Directory.Exists(pluginDir))
            yield break;

        foreach (var dllPath in Directory.GetFiles(pluginDir, "*.dll"))
        {
            Assembly assembly;
                try { assembly = Assembly.LoadFrom(dllPath); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DeepCrawl] Failed to load plugin assembly: {dllPath} — {ex.Message}");
                    continue;
                }

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                foreach (var le in ex.LoaderExceptions ?? [])
                    Console.WriteLine($"[DeepCrawl] Type load error in {dllPath}: {le?.Message}");
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }
            catch (TypeLoadException ex)
            {
                Console.WriteLine($"[DeepCrawl] Type load exception for {dllPath}: {ex.Message}");
                continue;
            }
            catch (FileLoadException ex)
            {
                Console.WriteLine($"[DeepCrawl] File load exception for {dllPath}: {ex.Message}");
                continue;
            }
            catch (BadImageFormatException ex)
            {
                Console.WriteLine($"[DeepCrawl] Bad image format for {dllPath}: {ex.Message}");
                continue;
            }

            foreach (var type in types)
            {
                if (!type.IsPublic || type.IsAbstract) continue;
                if (!typeof(IPlugin).IsAssignableFrom(type)) continue;

                IPlugin instance;
                try { instance = (IPlugin)Activator.CreateInstance(type)!; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DeepCrawl] Failed to instantiate plugin {type.Name} from {dllPath}: {ex.Message}");
                    continue;
                }
                yield return instance;
            }
        }
    }
}
