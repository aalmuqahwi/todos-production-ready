using Microsoft.Extensions.Options;

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
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
