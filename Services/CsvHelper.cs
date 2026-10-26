using System.Globalization;

namespace MkalInventoryApp.Services;

public static class CsvHelper
{
    public static string Field(object? value)
    {
        var s = value switch
        {
            null => "",
            DateTime d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            bool b => b ? "Yes" : "No",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };

        // Stop spreadsheet apps from running text such as "=1+1" as a formula.
        if (value is string && s.Length > 0 && "=+@".Contains(s[0])) s = "'" + s;

        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public static string Row(params object?[] values) => string.Join(",", values.Select(Field));
}
