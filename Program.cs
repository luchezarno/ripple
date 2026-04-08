using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ShellPilot.Services;
using ShellPilot.Tools;

namespace ShellPilot;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services
            .AddSingleton<ConsoleManager>()
            .AddSingleton<ProcessLauncher>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<ShellTools>()
            .WithTools<FileTools>();

        var host = builder.Build();

        // Initialize console manager
        var consoleManager = host.Services.GetRequiredService<ConsoleManager>();
        consoleManager.Initialize();

        await host.RunAsync();
    }
}
