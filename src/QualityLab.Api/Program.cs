using System.Diagnostics;

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
        // Vulnerabilidad intencional para que CodeQL la marque en el PR
        var process = Process.Start("bash", $"-c \"ping -c 1 {host}\"");
        return Results.Ok($"Ping enviado a {host}");
    }
    catch (Exception ex)
    {
        // Mala práctica: Catch genérico que silencia el error sin logging
        return Results.BadRequest();
    }
});

app.Run();
