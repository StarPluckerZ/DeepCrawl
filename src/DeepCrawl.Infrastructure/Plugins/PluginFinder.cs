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
                catch { continue; }

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            { types = ex.Types.Where(t => t is not null).ToArray()!; }
            catch (TypeLoadException) { continue; }
            catch (FileLoadException) { continue; }
            catch (BadImageFormatException) { continue; }

            foreach (var type in types)
            {
                if (!type.IsPublic || type.IsAbstract) continue;
                if (!typeof(IPlugin).IsAssignableFrom(type)) continue;

                IPlugin instance;
                try { instance = (IPlugin)Activator.CreateInstance(type)!; }
                catch { continue; }
                yield return instance;
            }
        }
    }
}
