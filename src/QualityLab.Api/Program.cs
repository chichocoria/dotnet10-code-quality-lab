using System.Diagnostics;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. Calidad (CA1822 / CS1998): Método async sin 'await' y variable local sin usar (CS0219)
app.MapGet("/weather", async () =>
{
    int unusedCounter = 42; 
    var temperatures = new List<int> { 20, 25, 30, 15 };

    // 2. Rendimiento (CA1826 / CA1860): Uso de Count() de LINQ en lugar de la propiedad .Count
    if (temperatures.Count() > 0)
    {
        return Results.Ok(temperatures.OrderBy(t => t).FirstOrDefault());
    }

    return Results.NoContent();
});

// 3. Seguridad y Calidad (CodeQL Command Injection / Descarte de excepciones)
app.MapGet("/ping", (string host) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(host) || !Regex.IsMatch(host, @"^[a-zA-Z0-9\.\-:]+$"))
        {
            return Results.BadRequest("Host inválido.");
        }

        var startInfo = new ProcessStartInfo("ping")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add(host);

        Process.Start(startInfo);
        return Results.Ok($"Ping enviado a {host}");
    }
    catch (Exception)
    {
        // Mala práctica: Catch genérico que silencia el error sin logging
        return Results.BadRequest();
    }
});

app.Run();
