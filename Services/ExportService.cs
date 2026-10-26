using System.Text;
using CommunityToolkit.Maui.Storage;

namespace MkalInventoryApp.Services;

/// <summary>Saves a CSV through the system "Save as" dialog (works on Windows, macOS and Android).</summary>
public static class ExportService
{
    public static async Task<(bool Ok, string Message)> SaveCsvAsync(string fileName, string csv)
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        using var stream = new MemoryStream(bytes);
        var result = await FileSaver.Default.SaveAsync(fileName, stream, CancellationToken.None);
        return result.IsSuccessful
            ? (true, $"Saved {fileName}")
            : (false, result.Exception?.Message ?? "Export cancelled.");
    }
}
