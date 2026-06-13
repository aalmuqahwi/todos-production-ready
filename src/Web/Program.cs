using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Polly;

using Serilog;

using Todos.Web.Handlers;
using Todos.Web.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, config) => config.ReadFrom.Configuration(context.Configuration)); // docs/steady-state.md

// docs/fail-fast.md
builder.Services
    .AddOptions<NotificationsOptions>()
    .BindConfiguration("Notifications")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<NotificationsResilienceHandler>();

builder.Services.AddHttpClient("Notifications", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<NotificationsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds); // docs/timeout.md
})
.AddHttpMessageHandler<NotificationsResilienceHandler>()
.AddResilienceHandler("notifications-pipeline", resilienceBuilder => // docs/test-harnesses.md
{
    resilienceBuilder.AddConcurrencyLimiter(10); // docs/bulkhead.md

    resilienceBuilder.AddRetry(new HttpRetryStrategyOptions // docs/retry.md, docs/create-back-pressure.md
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = static args => ValueTask.FromResult(
            args.Outcome.Exception is HttpRequestException ||
            (args.Outcome.Result is { IsSuccessStatusCode: false } r && (int)r.StatusCode >= 500)),
        DelayGenerator = static args =>
        {
            if (args.Outcome.Result is { StatusCode: System.Net.HttpStatusCode.ServiceUnavailable } response &&
                response.Headers.TryGetValues("Retry-After", out var values) &&
                int.TryParse(values.FirstOrDefault(), out var seconds))
            {
                return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromSeconds(seconds));
            }

            return ValueTask.FromResult<TimeSpan?>(null);
        }
    });

    resilienceBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions // docs/circuit-breaker.md
    {
        SamplingDuration = TimeSpan.FromSeconds(30),
        FailureRatio = 0.5,
        MinimumThroughput = 3,
        BreakDuration = TimeSpan.FromSeconds(15),
    });
});

// docs/global-exception-handling.md
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(name: "default", pattern: "{controller=Todos}/{action=Index}/{id?}");

app.Run();
