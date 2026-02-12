using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Client.Models;

namespace Client.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly HttpClient _httpClient;
        private const string DocumentServiceUrl = "http://localhost:8081/api/documents";

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public IActionResult Index()
        {
            return View();
        }

        // GET: /Home/Documents
        public async Task<IActionResult> Documents()
        {
            try
            {
                var response = await _httpClient.GetAsync(DocumentServiceUrl);
                var json = await response.Content.ReadAsStringAsync();
                var documents = JsonSerializer.Deserialize<List<DocumentMetadata>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return View(documents);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error loading documents: {ex.Message}";
                return View(new List<DocumentMetadata>());
            }
        }

        // GET: /Home/Upload
        public IActionResult Upload()
        {
            return View();
        }

        // POST: /Home/Upload
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file, string title)
        {
            if (file == null || file.Length == 0)
            {
                ViewBag.Error = "Please select a file";
                return View();
            }

            try
            {
                using var content = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();
                using var streamContent = new StreamContent(fileStream);

                content.Add(streamContent, "file", file.FileName);
                content.Add(new StringContent(title ?? file.FileName), "title");

                var response = await _httpClient.PostAsync($"{DocumentServiceUrl}/upload", content);
                var result = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("Documents");
                }
                else
                {
                    ViewBag.Error = $"Upload failed: {result}";
                    return View();
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error: {ex.Message}";
                return View();
            }
        }

        // GET: /Home/Versions/{documentId}
        public async Task<IActionResult> Versions(string documentId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{DocumentServiceUrl}/{documentId}/versions");
                var json = await response.Content.ReadAsStringAsync();
                var versions = JsonSerializer.Deserialize<List<DocumentVersion>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                ViewBag.DocumentId = documentId;
                return View(versions);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error loading versions: {ex.Message}";
                return View(new List<DocumentVersion>());
            }
        }

        // GET: /Home/AddVersion/{documentId}
        public IActionResult AddVersion(string documentId)
        {
            ViewBag.DocumentId = documentId;
            return View();
        }

        // POST: /Home/AddVersion/{documentId}
        [HttpPost]
        public async Task<IActionResult> AddVersion(string documentId, IFormFile file, DateTime validFrom, DateTime? validTo)
        {
            if (file == null || file.Length == 0)
            {
                ViewBag.Error = "Please select a file";
                ViewBag.DocumentId = documentId;
                return View();
            }

            try
            {
                using var content = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();
                using var streamContent = new StreamContent(fileStream);

                content.Add(streamContent, "file", file.FileName);
                content.Add(new StringContent(validFrom.ToString("yyyy-MM-ddTHH:mm:ss")), "validFrom");

                if (validTo.HasValue)
                {
                    content.Add(new StringContent(validTo.Value.ToString("yyyy-MM-ddTHH:mm:ss")), "validTo");
                }

                var response = await _httpClient.PostAsync($"{DocumentServiceUrl}/{documentId}/versions/upload", content);

                if (response.IsSuccessStatusCode)
                {
                    return RedirectToAction("Versions", new { documentId });
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    ViewBag.Error = $"Upload failed: {error}";
                    ViewBag.DocumentId = documentId;
                    return View();
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error: {ex.Message}";
                ViewBag.DocumentId = documentId;
                return View();
            }
        }

        // POST: /Home/ParseVersion/{versionId}
        [HttpPost]
        public async Task<IActionResult> ParseVersion(string versionId, string documentId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{DocumentServiceUrl}/versions/{versionId}/parse", null);
                return RedirectToAction("Versions", new { documentId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Parse error: {ex.Message}";
                return RedirectToAction("Versions", new { documentId });
            }
        }

        // GET: /Home/VersionChunks/{versionId}
        public async Task<IActionResult> VersionChunks(string versionId, string documentId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{DocumentServiceUrl}/versions/{versionId}/chunks");
                var json = await response.Content.ReadAsStringAsync();
                var chunks = JsonSerializer.Deserialize<List<DocumentChunk>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                ViewBag.VersionId = versionId;
                ViewBag.DocumentId = documentId;
                return View(chunks);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error loading chunks: {ex.Message}";
                return View(new List<DocumentChunk>());
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}