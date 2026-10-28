using System;
using System.IO;

namespace ReplayLite
{
    sealed class CaptureUnavailableException : IOException
    {
        internal CaptureUnavailableException(string message) : base(message) { }
        internal CaptureUnavailableException(string message, Exception inner) : base(message, inner) { }
    }
    static class CaptureRecovery
    {
        internal static bool UnsupportedEncoder(string message)
        {
            if (string.IsNullOrEmpty(message) || IsTransient(message)) return false;
            string text = message.ToLowerInvariant();
            return text.Contains("_nvenc") && (text.Contains("codec not supported") || text.Contains("doesn't support required nvenc features") || text.Contains("driver does not support the required nvenc api version"));
        }
        internal static bool IsTransient(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            string text = message.ToLowerInvariant();
            if (text.Contains("desktop duplication access denied")) return true;
            foreach (string code in new[] { "887a0026", "887a0005", "887a0007", "887a0028", "887a0029", "887a002b", "dxgi_error_access_lost", "dxgi_error_device_removed", "dxgi_error_device_reset", "dxgi_error_session_disconnected" })
                if (text.Contains(code)) return true;
            return text.Contains("80070005") && (text.Contains("ddagrab") || text.Contains("duplicateoutput"));
        }
        internal static int RetrySeconds(int failures)
        { return Math.Min(30, 1 << Math.Min(5, Math.Max(0, failures))); }
        internal static bool ShouldRetry(Exception error)
        { return error is CaptureUnavailableException || error is TimeoutException || (!(error is OperationCanceledException) && IsTransient(error.ToString())); }
    }
}
