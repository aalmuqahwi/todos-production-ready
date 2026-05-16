using Microsoft.Extensions.Options;

using Todos.Web.Handlers;
using Todos.Web.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<NotificationsOptions>()
    .BindConfiguration("Notifications")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient("Notifications", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<NotificationsOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(name: "default", pattern: "{controller=Todos}/{action=Index}/{id?}");

app.Run();
