using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        // POST: /Home/Parse/{id}
        [HttpPost]
        public async Task<IActionResult> Parse(string id)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{DocumentServiceUrl}/{id}/parse", null);
                return RedirectToAction("Documents");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Parse error: {ex.Message}";
                return RedirectToAction("Documents");
            }
        }

        // GET: /Home/Chunks/{id}
        public async Task<IActionResult> Chunks(string id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{DocumentServiceUrl}/{id}/chunks");
                var json = await response.Content.ReadAsStringAsync();
                var chunks = JsonSerializer.Deserialize<List<DocumentChunk>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                ViewBag.DocumentId = id;
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