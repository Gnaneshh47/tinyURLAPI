using Dapper;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using tinyURLAPI.Data;
using tinyURLAPI.Models;

namespace tinyURLAPI.repo
{
    public class ShortUrlRepository
    {
        private readonly DataAccess _db;

        public ShortUrlRepository(DataAccess db)
        {
            _db = db;
        }

        public async Task<IEnumerable<ShortUrl>> GetAllAsync()
        {
            const string sql = "SELECT * FROM tbl_tiny_url ORDER BY Id DESC";

            using var conn = _db.CreateConnection();
            return await conn.QueryAsync<ShortUrl>(sql);
        }

        public async Task<ShortUrl> GetByCodeAsync(string code)
        {
            const string sql = "SELECT * FROM tbl_tiny_url WHERE ShortCode = @ShortCode";

            using var conn = _db.CreateConnection();
            return await conn.QueryFirstOrDefaultAsync<ShortUrl>(sql, new { ShortCode = code });
        }

        public async Task<int> CreateAsync(CreateShortUrlRequest url)
        {
            string shortCode = ShortCodeGenerator.GenerateShortCode(url.OriginalUrl);

            string sql = @"
                SET NOCOUNT ON;

                INSERT INTO tbl_tiny_url (ShortCode, OriginalUrl, IsPrivate)
                OUTPUT inserted.Id
                VALUES (@ShortCode, @OriginalUrl, @IsPrivate);
            ";

            using var conn = _db.CreateConnection();

            var newId = await conn.QuerySingleAsync<int>(sql, new
            {
                ShortCode = shortCode,
                OriginalUrl = url.OriginalUrl.Trim(),
                url.IsPrivate
            });

            return newId;
        }

        public async Task<int> UpdateAsync(ShortUrl url)
        {
            const string sql = @"
                UPDATE tbl_tiny_url
                SET VisitCount = @VisitCount
                WHERE ShortCode = @ShortCode
            ";

            using var conn = _db.CreateConnection();
            return await conn.ExecuteAsync(sql, url);
        }

        public async Task<int> DeleteAsync(string code)
        {
            const string sql = "DELETE FROM tbl_tiny_url WHERE ShortCode = @ShortCode";

            using var conn = _db.CreateConnection();
            return await conn.ExecuteAsync(sql, new { ShortCode = code });
        }

        public static class ShortCodeGenerator
        {
            private const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

            // Generate short code from hash (6-8 chars)
            public static string GenerateShortCode(string url, int length = 6)
            {
                if (string.IsNullOrEmpty(url))
                    return RandomString(length);

                using var sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(url + DateTime.UtcNow.Ticks));

                var sb = new StringBuilder();
                for (int i = 0; i < length; i++)
                {
                    sb.Append(chars[hash[i] % chars.Length]);
                }
                return sb.ToString();
            }

            // Fallback random string
            public static string RandomString(int length = 6)
            {
                var rnd = new Random();
                return new string(Enumerable.Range(0, length)
                    .Select(_ => chars[rnd.Next(chars.Length)]).ToArray());
            }
        }

    }
}
