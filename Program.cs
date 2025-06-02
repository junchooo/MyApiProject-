// Add these using statements at the very top of your Program.cs file
using System.Reflection;
using System.IO;
// If you use builder.Logging.AddLog4Net(), you might need:
// using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// --- log4net Configuration ---
// Get the log4net repository. Handle potential null from GetEntryAssembly().
var entryAssembly = Assembly.GetEntryAssembly();
// Fallback to typeof(Program).Assembly if GetEntryAssembly() is null.
// This is a common pattern to ensure an assembly is provided.
var repositoryAssembly = entryAssembly ?? typeof(Program).Assembly; 
var logRepository = log4net.LogManager.GetRepository(repositoryAssembly);

// Tell log4net to configure itself using the log4net.config file.
// Make sure "log4net.config" is in your project root and its "Copy to Output Directory" property is set to "Copy if newer".
log4net.Config.XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));
// ----------------------------

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDataProtection();
// Optional: If you want to integrate log4net with ASP.NET Core's ILogger framework.
// This allows ILogger<T> instances to also write to log4net appenders.
// You might need to add the Microsoft.Extensions.Logging.Log4Net.AspNetCore NuGet package for AddLog4Net().
// builder.Logging.ClearProviders(); // Optional: Clears other logging providers like Console, if you only want log4net
// builder.Logging.AddLog4Net();     // This tells ASP.NET Core logging to also use log4net

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Your existing Minimal API endpoint for weatherforecast
var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.MapControllers(); // You correctly have this to map controller routes

// Example: Log that the application is starting up using log4net directly
// This helps confirm log4net is initialized and working.
var logger = log4net.LogManager.GetLogger(typeof(Program)); // Or use System.Reflection.MethodBase.GetCurrentMethod().DeclaringType
logger.Info("Application Starting Up...");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
