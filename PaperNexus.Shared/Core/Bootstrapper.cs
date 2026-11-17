using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.Reflection;

namespace PaperNexus.Core;

/// <summary>
/// Implement this interface on a class to have it automatically registered as
/// <typeparamref name="TService"/> singleton via <see cref="Bootstrapper.AddServicesFrom"/>.
/// </summary>
public interface IAddSingleton<TService>
{
}

/// <summary>
/// Implement this interface on a hosted service class to have it registered as both a singleton
/// (accessible via <typeparamref name="TService"/>) and as an <see cref="IHostedService"/>
/// via <see cref="Bootstrapper.AddServicesFrom"/>.
/// </summary>
public interface IAddHostedSingleton<TService>
{
}

public static class Bootstrapper
{
    // Scans the given assembly for types that opt-in to auto-registration via marker interfaces:
    //   IAddSingleton<T>         → registers the impl as T singleton
    //   IAddHostedSingleton<T>   → registers the impl as T singleton + IHostedService
    //   IScheduleScopedJob       → wraps the impl in a ScheduledJobHostedService<T> hosted service
    public static IServiceCollection AddServicesFrom(this IServiceCollection services, Assembly assembly)
    {
        var openSingleton = typeof(IAddSingleton<>);
        var openHosted = typeof(IAddHostedSingleton<>);

        foreach (var implType in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            // Register each IAddSingleton<T> implementation as its service type
            foreach (var iface in implType.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openSingleton))
            {
                var serviceType = iface.GetGenericArguments()[0];
                services.AddSingleton(serviceType, implType);
            }

            // Register each IAddHostedSingleton<T> impl as both its service type and IHostedService.
            // The concrete type is registered first so both service registrations resolve the same instance.
            foreach (var iface in implType.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openHosted))
            {
                var serviceType = iface.GetGenericArguments()[0];
                services.AddSingleton(implType);
                services.AddSingleton(serviceType, sp => sp.GetRequiredService(implType));
                services.AddSingleton<IHostedService>(sp => (IHostedService)sp.GetRequiredService(implType));
            }
        }

        // Discover all IScheduleScopedJob implementations and wrap each in a typed hosted service.
        // Reflection is used to call the generic helper because the job type is only known at runtime.
        var jobTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IScheduleScopedJob).IsAssignableFrom(t))
            .ToList();

        foreach (var jobType in jobTypes)
        {
            var method = typeof(Bootstrapper)
                .GetMethod(nameof(AddScheduledJobHostedService), BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Bootstrapper helper method '{nameof(AddScheduledJobHostedService)}' not found via reflection.");
            var addMethod = method.MakeGenericMethod(jobType);
            addMethod.Invoke(null, new object[] { services });
        }

        return services;
    }

    private static void AddScheduledJobHostedService<TJob>(IServiceCollection services)
        where TJob : IScheduleScopedJob
    {
        services.AddHostedService<ScheduledJobHostedService<TJob>>();
    }
}
