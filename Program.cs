var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { ok = true, message = "Hello from Azure App Service!" }));
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();