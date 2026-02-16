using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentService.Models;

namespace DocumentService.Services
{
    public static class StorageHelper
    {
        private static readonly string StorageRoot = Path.Combine(Directory.GetCurrentDirectory(), "storage");

        public static string DocumentsFolder => Path.Combine(StorageRoot, "documents");
        public static string ParsedFolder => Path.Combine(StorageRoot, "parsed");
        public static string EmbeddingsFolder => Path.Combine(StorageRoot, "embeddings");

        public static string GetDocumentFolder(string documentId, string documentTitle)
        {
            // Sanitize title for folder name
            var sanitizedTitle = SanitizeFolderName(documentTitle);
            var folderName = $"{sanitizedTitle}_{documentId.Substring(0, 8)}"; // e.g., "Contract_a1b2c3d4"

            var folder = Path.Combine(DocumentsFolder, folderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string GetVersionFilePath(string documentId, string documentTitle, int versionNumber, string extension)
        {
            var folder = GetDocumentFolder(documentId, documentTitle);
            return Path.Combine(folder, $"v{versionNumber}{extension}");
        }

        public static string GetParsedFilePath(string versionId)
        {
            return Path.Combine(ParsedFolder, $"{versionId}.json");
        }

        public static async Task SaveParsedDataAsync(string versionId, ParsedData data)
        {
            var filePath = GetParsedFilePath(versionId);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }

        public static async Task<ParsedData> LoadParsedDataAsync(string versionId)
        {
            var filePath = GetParsedFilePath(versionId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<ParsedData>(json);
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Untitled";

            // Remove invalid characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(name.Split(invalidChars));

            // Replace spaces with underscores
            sanitized = Regex.Replace(sanitized, @"\s+", "_");

            // Limit length
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            return sanitized;
        }
    }

    public class ParsedData
    {
        public string VersionId { get; set; }
        public ParsedChunk[] Chunks { get; set; }
    }

    public class ParsedChunk
    {
        public int Index { get; set; }
        public string Text { get; set; }
    }
}