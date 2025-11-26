using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AzureSql");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//
// ----------------------------------------------------
// REDIRECT: GET /{shortCode} 
// ----------------------------------------------------
app.MapGet("/{code}", async (string code) =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        SELECT OriginalUrl, VisitCount, ExpiresAt, IsActive
        FROM ShortUrls
        WHERE ShortCode = @code
    ", conn);

    cmd.Parameters.AddWithValue("@code", code);

    using var reader = await cmd.ExecuteReaderAsync();

    if (!reader.Read())
        return Results.NotFound(new { message = "Short code not found" });

    string originalUrl = reader.GetString(0);
    int visitCount = reader.GetInt32(1);
    DateTime? expiresAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
    bool isActive = reader.GetBoolean(3);

    // Check active status
    if (!isActive)
        return Results.BadRequest(new { message = "This short URL is disabled" });

    // Check expiry
    if (expiresAt != null && expiresAt < DateTime.UtcNow)
        return Results.BadRequest(new { message = "This short URL has expired" });

    reader.Close();

    // Update visit count
    var updateCmd = new SqlCommand(@"
        UPDATE ShortUrls
        SET VisitCount = @count
        WHERE ShortCode = @code
    ", conn);

    updateCmd.Parameters.AddWithValue("@count", visitCount + 1);
    updateCmd.Parameters.AddWithValue("@code", code);

    await updateCmd.ExecuteNonQueryAsync();

    return Results.Redirect(originalUrl);
});


//
// ----------------------------------------------------
// CREATE SHORT URL: POST /api/shorten
// ----------------------------------------------------
app.MapPost("/api/shorten", async (CreateShortRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.OriginalUrl))
        return Results.BadRequest(new { message = "OriginalUrl is required" });

    if (!Uri.TryCreate(req.OriginalUrl, UriKind.Absolute, out var uri))
        return Results.BadRequest(new { message = "Invalid URL" });

    string originalUrl = uri.ToString().TrimEnd('/');

    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    // Check if URL already exists (optional behavior)
    var findCmd = new SqlCommand(@"
        SELECT ShortCode FROM ShortUrls
        WHERE OriginalUrl = @url AND IsActive = 1
    ", conn);

    findCmd.Parameters.AddWithValue("@url", originalUrl);
    var existing = await findCmd.ExecuteScalarAsync();

    if (existing != null)
    {
        return Results.Ok(new
        {
            shortCode = existing.ToString(),
            originalUrl
        });
    }

    // Generate unique short code
    string code;
    do
    {
        code = ShortCodeGenerator.Generate(6);

        var checkCmd = new SqlCommand(@"
            SELECT COUNT(*) FROM ShortUrls WHERE ShortCode = @code
        ", conn);

        checkCmd.Parameters.AddWithValue("@code", code);

        int count = (int)await checkCmd.ExecuteScalarAsync();

        if (count == 0) break;

    } while (true);

    // INSERT
    var insertCmd = new SqlCommand(@"
        INSERT INTO ShortUrls (ShortCode, OriginalUrl, CreatedAt, ExpiresAt, IsActive)
        VALUES (@code, @url, SYSUTCDATETIME(), @expires, 1)
    ", conn);

    insertCmd.Parameters.AddWithValue("@code", code);
    insertCmd.Parameters.AddWithValue("@url", originalUrl);
    insertCmd.Parameters.AddWithValue("@expires", (object?)req.ExpiresAt ?? DBNull.Value);

    await insertCmd.ExecuteNonQueryAsync();

    return Results.Created($"/{code}", new
    {
        shortCode = code,
        originalUrl,
        expiresAt = req.ExpiresAt
    });
});


//
// ----------------------------------------------------
// GET INFO: GET /api/urls/{code}
// ----------------------------------------------------
app.MapGet("/api/urls/{code}", async (string code) =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        SELECT Id, ShortCode, OriginalUrl, CreatedAt, ExpiresAt, VisitCount, IsActive
        FROM ShortUrls
        WHERE ShortCode = @code
    ", conn);

    cmd.Parameters.AddWithValue("@code", code);

    using var reader = await cmd.ExecuteReaderAsync();

    if (!reader.Read())
        return Results.NotFound();

    return Results.Ok(new
    {
        Id = reader.GetInt32(0),
        ShortCode = reader.GetString(1),
        OriginalUrl = reader.GetString(2),
        CreatedAt = reader.GetDateTime(3),
        ExpiresAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
        VisitCount = reader.GetInt32(5),
        IsActive = reader.GetBoolean(6)
    });
});


//
// ----------------------------------------------------
// LIST ALL: GET /api/urls
// ----------------------------------------------------
app.MapGet("/api/urls", async () =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        SELECT ShortCode, OriginalUrl, CreatedAt, VisitCount, IsActive
        FROM ShortUrls
        ORDER BY CreatedAt DESC
    ", conn);

    var list = new List<object>();

    using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        list.Add(new
        {
            ShortCode = reader.GetString(0),
            OriginalUrl = reader.GetString(1),
            CreatedAt = reader.GetDateTime(2),
            VisitCount = reader.GetInt32(3),
            IsActive = reader.GetBoolean(4)
        });
    }

    return Results.Ok(list);
});


//
// ----------------------------------------------------
// SOFT DELETE / DISABLE URL: PUT /api/urls/{code}/disable
// ----------------------------------------------------
app.MapPut("/api/urls/{code}/disable", async (string code) =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        UPDATE ShortUrls SET IsActive = 0 WHERE ShortCode = @code
    ", conn);

    cmd.Parameters.AddWithValue("@code", code);

    int rows = await cmd.ExecuteNonQueryAsync();

    return rows > 0 ? Results.Ok(new { message = "Short URL disabled" }) : Results.NotFound();
});

app.Run();


//
// ----------------- MODELS -----------------

public record CreateShortRequest(string OriginalUrl, DateTime? ExpiresAt);


//
// ----------------- SHORT CODE GENERATOR -----------------

public static class ShortCodeGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Generate(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);

        var sb = new StringBuilder(length);
        foreach (var b in bytes)
            sb.Append(Alphabet[b % Alphabet.Length]);

        return sb.ToString();
    }
}
