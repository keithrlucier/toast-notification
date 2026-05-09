namespace ToastRevival.Api.Utilities;

public static class CsvHelper
{
    public static string Cell(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
