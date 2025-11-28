namespace tinyURLAPI.Models
{
   public class ShortUrl
    {
        public int Id { get; set; }
        public string ShortCode { get; set; }
        public string OriginalUrl { get; set; }
        public bool IsPrivate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ExpiryTime { get; set; }
        public int VisitCount { get; set; }
    }

    public class CreateShortUrlRequest
    {
        public string OriginalUrl { get; set; }
        public bool IsPrivate { get; set; }
    }
}
