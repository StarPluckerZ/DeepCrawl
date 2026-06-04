namespace DeepSeekSDK;

public class DeepSeekApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public DeepSeekApiException(int statusCode, string? responseBody)
        : base($"DeepSeek API returned {statusCode}: {Truncate(responseBody, 200)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength) return value;
        return value[..maxLength] + "...";
    }
}
