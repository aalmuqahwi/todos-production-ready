using Todos.Notifications.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ServiceOptions>()
    .BindConfiguration("Service")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
