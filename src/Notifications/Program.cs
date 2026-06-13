using Microsoft.AspNetCore.RateLimiting;
using Serilog;

using Todos.Notifications.Handlers;
using Todos.Notifications.Options;
using Todos.Notifications.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, config) => config.ReadFrom.Configuration(context.Configuration)); // docs/steady-state.md

// docs/fail-fast.md
builder.Services
    .AddOptions<ServiceOptions>()
    .BindConfiguration("Service")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// docs/let-it-crash.md
builder.Services.AddSingleton<NotificationCounter>();
builder.Services.AddOptions<NotificationSummaryOptions>().BindConfiguration("NotificationSummaryService");
builder.Services.AddHostedService<NotificationSummaryService>();

// docs/shed-load.md
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("notifications", limiter =>
    {
        limiter.Window = TimeSpan.FromSeconds(1);
        limiter.PermitLimit = 10;
        limiter.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;
});

// docs/global-exception-handling.md
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers();

var app = builder.Build();

app.UseRateLimiter();
app.UseExceptionHandler();
app.MapControllers();

app.Run();
