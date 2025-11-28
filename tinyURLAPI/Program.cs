using tinyURLAPI.Data;
using tinyURLAPI.Models;
using tinyURLAPI.repo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DataAccess>();
builder.Services.AddSingleton<ShortUrlRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AllowAll");

// GET all
app.MapGet("/api/shorturls", async (ShortUrlRepository repo) =>
{
    var list = await repo.GetAllAsync();
    return Results.Ok(list);
});

// CREATE
app.MapPost("/api/shorturls", async (CreateShortUrlRequest model, ShortUrlRepository repo) =>
{
    var result = await repo.CreateAsync(model);
    return result > 0 ? Results.Ok("Created") : Results.BadRequest();
});

// DELETE
app.MapDelete("/api/shorturls/{code}", async (string code, ShortUrlRepository repo) =>
{
    var result = await repo.DeleteAsync(code);
    return result > 0 ? Results.Ok("Deleted") : Results.NotFound();
});


app.MapGet("/{shortCode}", async (string shortCode, ShortUrlRepository repo) =>
{
    var url = await repo.GetByCodeAsync(shortCode);
    if (url == null)
        return Results.NotFound("Short URL not found");

    if (url.ExpiryTime < DateTime.UtcNow)
        return Results.BadRequest("Short URL has expired");

    // Increment visit count
    url.VisitCount++;
    await repo.UpdateAsync(url);

    return Results.Redirect(url.OriginalUrl);
});

app.Run();