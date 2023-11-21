
var builder = DistributedApplication.CreateBuilder(args);

builder.AddRabbitMQContainer("guest", 5672, "guest");

builder.AddContainer("arm64", "compiler/dev");

builder.AddProject<Projects.Blazemoji>("blazemoji");

builder.Build().Run();
