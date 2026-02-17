using System;
using System.IO;
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

        // ─── Document folder ───────────────────────────────────────────────
        public static string GetDocumentFolder(string documentId, string documentTitle)
        {
            var sanitizedTitle = SanitizeFolderName(documentTitle);
            var folderName = $"{sanitizedTitle}_{documentId.Substring(0, 8)}";
            var folder = Path.Combine(DocumentsFolder, folderName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        // ─── Version file path (fizicki fajl) ──────────────────────────────
        public static string GetVersionFilePath(string documentId, string documentTitle, int versionNumber, string extension)
        {
            var folder = GetDocumentFolder(documentId, documentTitle);
            return Path.Combine(folder, $"v{versionNumber}{extension}");
        }

        // ─── Metadata folder i path ─────────────────────────────────────────
        public static string GetMetadataFolder(string documentId, string documentTitle)
        {
            var docFolder = GetDocumentFolder(documentId, documentTitle);
            var metadataFolder = Path.Combine(docFolder, "metadata");
            Directory.CreateDirectory(metadataFolder);
            return metadataFolder;
        }

        public static async Task SaveVersionMetadataAsync(string documentId, string documentTitle, DocumentVersion version)
        {
            var metadataFolder = GetMetadataFolder(documentId, documentTitle);
            var filePath = Path.Combine(metadataFolder, $"v{version.VersionNumber}.json");

            var json = JsonSerializer.Serialize(new
            {
                version.VersionId,
                version.VersionNumber,
                version.FilePath,
                version.Extension,
                version.UploadedAt,
                version.ValidFrom,
                version.ValidTo,
                version.IsParsed
            }, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(filePath, json);
        }

        // ─── Parsed file paths ──────────────────────────────────────────────

        // Koristi se za LoadParsedDataAsync (API čita po versionId)
        public static string GetParsedFilePathById(string versionId)
        {
            return Path.Combine(ParsedFolder, $"{versionId}.json");
        }

        // Koristi se za human-readable naziv
        private static string GetParsedFilePathByName(string documentTitle, int versionNumber)
        {
            var sanitizedTitle = SanitizeFolderName(documentTitle);
            return Path.Combine(ParsedFolder, $"{sanitizedTitle}_v{versionNumber}.json");
        }

        // Čuva na oba mesta: po versionId (za API) i po imenu (human-readable)
        public static async Task SaveParsedDataAsync(string versionId, ParsedData data, string documentTitle, int versionNumber)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(GetParsedFilePathById(versionId), json);
            await File.WriteAllTextAsync(GetParsedFilePathByName(documentTitle, versionNumber), json);
        }

        // Čita po versionId (koristi API)
        public static async Task<ParsedData> LoadParsedDataAsync(string versionId)
        {
            var filePath = GetParsedFilePathById(versionId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<ParsedData>(json);
        }

        // ─── Sanitize ──────────────────────────────────────────────────────
        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Untitled";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Concat(name.Split(invalidChars));
            sanitized = Regex.Replace(sanitized, @"\s+", "_");

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