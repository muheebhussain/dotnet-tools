 // Normalize ETag string to ensure it is stored without surrounding double quotes.
 // Handles weak etag prefix (W/) and strips surrounding quotes if present.
 private static string? NormalizeEtag(string? etag)
 {
     if (string.IsNullOrWhiteSpace(etag)) return etag;
     var s = etag.Trim();

     // Remove weak prefix
     if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
     {
         s = s.Substring(2);
     }

     // Trim surrounding quotes, if any
     s = s.Trim();
     if (s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\""))
     {
         s = s.Substring(1, s.Length - 2);
     }

     return s;
 }
