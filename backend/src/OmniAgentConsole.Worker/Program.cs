using Microsoft.Extensions.Hosting;
using OmniAgentConsole.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration["IsWorker"] = "true";
builder.Services.AddInfrastructure(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
