using System.Text;

namespace StringSearch.Model;

public static class Display
{
    /// <summary>A string value as a one-line C#-style literal: quotes, backslashes and control characters escaped.</summary>
    public static string Quote(string value, int maxLength = int.MaxValue)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
            if (sb.Length > maxLength)
                return sb.Append("…\"").ToString();
        }
        return sb.Append('"').ToString();
    }
}
