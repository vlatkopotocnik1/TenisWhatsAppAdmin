using System.Diagnostics;

public class LoggingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHandler> _logger;

    public LoggingHandler(ILogger<LoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("Outgoing HTTP {Method} {Uri}", request.Method, request.RequestUri);

        try
        {
            var resp = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            if (!resp.IsSuccessStatusCode || sw.ElapsedMilliseconds > 1000) // only info if slow or error
            {
                var len = resp.Content?.Headers.ContentLength ?? -1;
                _logger.LogInformation("Received {StatusCode} from {Uri} in {ElapsedMs}ms (BodyLen={BodyLen})",
                    (int)resp.StatusCode, request.RequestUri, sw.Elapsed.TotalMilliseconds, len);
            }
            else
            {
                // keep a lightweight debug record
                _logger.LogDebug("Received {StatusCode} from {Uri} in {ElapsedMs}ms", (int)resp.StatusCode, request.RequestUri, sw.Elapsed.TotalMilliseconds);
            }

            return resp;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error calling {Method} {Uri} after {ElapsedMs}ms", request.Method, request.RequestUri, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "N/A";
        var p = phone.Trim();
        if (p.Length <= 4) return new string('*', p.Length);
        return $"***{p[^4..]}"; // show only last 4 digits
    }
}
