using LGSTrayPrimitives;
using LGSTrayUI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LGSTrayUI.Extensions;

internal static class ServiceCollectionExtensions {
    public static void AddMQTTClient(this IServiceCollection services, IConfiguration configs) {
        var settings = configs.Get<AppSettings>() ?? throw new System.Exception("Settings null in ServiceCollectionExtensions !");

        if (settings.MQTT.Enabled) {
            services.AddHostedService<MQTTService>();
            DiagnosticLogger.Log("MQTT service enabled");
        }
    }
}
