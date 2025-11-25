using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AzureSql");


// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapGet("/{code:length(6)}", async (string code) =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        SELECT OriginalUrl, AccessCount 
        FROM UrlMappings WHERE ShortCode = @code
    ", conn);

    cmd.Parameters.AddWithValue("@code", code);

    using var reader = await cmd.ExecuteReaderAsync();

    if (!reader.Read())
        return Results.NotFound(new { message = "Short code not found" });

    var originalUrl = reader.GetString(0);
    var accessCount = reader.GetInt32(1);

    reader.Close();

    // update access count
    var updateCmd = new SqlCommand(@"
        UPDATE UrlMappings SET 
           AccessCount = @count, 
           LastAccessedAt = SYSUTCDATETIME()
        WHERE ShortCode = @code
    ", conn);

    updateCmd.Parameters.AddWithValue("@count", accessCount + 1);
    updateCmd.Parameters.AddWithValue("@code", code);
    await updateCmd.ExecuteNonQueryAsync();

    return Results.Redirect(originalUrl);
});


//
// -----------------------------------
// CREATE SHORT URL: POST /api/shorten
// -----------------------------------
app.MapPost("/api/shorten", async (CreateShortRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.OriginalUrl))
        return Results.BadRequest(new { message = "OriginalUrl is required" });

    if (!Uri.TryCreate(req.OriginalUrl, UriKind.Absolute, out var uri))
        return Results.BadRequest(new { message = "Invalid URL" });

    string originalUrl = uri.ToString().TrimEnd('/');

    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    // If already exists return old short code
    var findCmd = new SqlCommand(@"
        SELECT ShortCode FROM UrlMappings WHERE OriginalUrl = @url
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
            SELECT COUNT(*) FROM UrlMappings WHERE ShortCode = @code
        ", conn);

        checkCmd.Parameters.AddWithValue("@code", code);

        var count = (int)await checkCmd.ExecuteScalarAsync();

        if (count == 0) break;

    } while (true);

    // Insert record
    var insertCmd = new SqlCommand(@"
        INSERT INTO UrlMappings (ShortCode, OriginalUrl, CreatedAt)
        VALUES (@code, @url, SYSUTCDATETIME())
    ", conn);

    insertCmd.Parameters.AddWithValue("@code", code);
    insertCmd.Parameters.AddWithValue("@url", originalUrl);

    await insertCmd.ExecuteNonQueryAsync();

    return Results.Created($"/{code}", new { shortCode = code, originalUrl });
});


//
// -----------------------------------
// GET INFO: GET /api/urls/{code}
// -----------------------------------
app.MapGet("/api/urls/{code:length(6)}", async (string code) =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        SELECT ShortCode, OriginalUrl, CreatedAt, AccessCount, LastAccessedAt
        FROM UrlMappings WHERE ShortCode = @code
    ", conn);

    cmd.Parameters.AddWithValue("@code", code);

    using var reader = await cmd.ExecuteReaderAsync();

    if (!reader.Read())
        return Results.NotFound();

    return Results.Ok(new
    {
        ShortCode = reader.GetString(0),
        OriginalUrl = reader.GetString(1),
        CreatedAt = reader.GetDateTime(2),
        AccessCount = reader.GetInt32(3),
        LastAccessedAt = reader.IsDBNull(4)? (DateTime?)null: reader.GetDateTime(4)
});
});


//
// -----------------------------------
// LIST ALL: GET /api/urls
// -----------------------------------
app.MapGet("/api/urls", async () =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        SELECT ShortCode, OriginalUrl, CreatedAt, AccessCount 
        FROM UrlMappings ORDER BY CreatedAt DESC
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
            AccessCount = reader.GetInt32(3)
        });
    }

    return Results.Ok(list);
});


//
// -----------------------------------
// DELETE: DELETE /api/urls/{code}
// -----------------------------------
app.MapDelete("/api/urls/{code:length(6)}", async (string code) =>
{
    using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        DELETE FROM UrlMappings WHERE ShortCode = @code
    ", conn);

    cmd.Parameters.AddWithValue("@code", code);

    int rows = await cmd.ExecuteNonQueryAsync();

    return rows > 0 ? Results.NoContent() : Results.NotFound();
});

app.Run();

public record CreateShortRequest(string OriginalUrl);

public static class ShortCodeGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Generate(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);

        var sb = new StringBuilder(length);
        foreach (var b in bytes)
        {
            sb.Append(Alphabet[b % Alphabet.Length]);
        }
        return sb.ToString();
    }
}