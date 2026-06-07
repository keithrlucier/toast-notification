using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToastRevival.AgentHealthService;

// ToastNotificationHealth: a LocalSystem Windows service that phones home so the
// admin dashboard shows a device online based on machine-up, independent of any
// interactive logon. AddWindowsService wires SCM integration + Windows Event Log.
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ToastNotificationHealth";
});

builder.Services.AddHostedService<HealthReporter>();

builder.Build().Run();
