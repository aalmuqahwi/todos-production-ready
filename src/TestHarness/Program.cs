using Todos.TestHarness.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<HarnessState>();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();
app.Run("http://localhost:5002");
