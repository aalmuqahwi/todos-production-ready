using Todos.Notifications.Handlers;
using Todos.Notifications.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ServiceOptions>()
    .BindConfiguration("Service")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers();

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();

app.Run();
