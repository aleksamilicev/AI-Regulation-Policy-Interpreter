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
        private const string RetrievalServiceUrl = "http://localhost:8082/api/search";
        private const string LLMServiceUrl = "http://localhost:8083/api/llm";

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

        #region Retrieval Service
        // GET: /Home/Search/{documentId}/{versionId}
        public IActionResult Search(string documentId, string versionId)
        {
            ViewBag.DocumentId = documentId;
            ViewBag.VersionId = versionId;
            return View();
        }

        // POST: /Home/Search
        [HttpPost]
        public async Task<IActionResult> Search(string documentId, string versionId, string query, int topK = 5)
        {
            try
            {
                var request = new SearchRequest
                {
                    Query = query,
                    DocumentId = documentId,
                    VersionId = versionId,
                    TopK = topK
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(RetrievalServiceUrl, content);
                var resultJson = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var searchResponse = JsonSerializer.Deserialize<SearchResponse>(resultJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    ViewBag.DocumentId = documentId;
                    ViewBag.VersionId = versionId;
                    ViewBag.Query = query;
                    ViewBag.SearchResponse = searchResponse;

                    return View();
                }
                else
                {
                    ViewBag.Error = $"Search failed: {resultJson}";
                    ViewBag.DocumentId = documentId;
                    ViewBag.VersionId = versionId;
                    return View();
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error: {ex.Message}";
                ViewBag.DocumentId = documentId;
                ViewBag.VersionId = versionId;
                return View();
            }
        }
        #endregion

        #region LLM Service
        // GET: /Home/Ask/{documentId}/{versionId}
        public IActionResult Ask(string documentId, string versionId)
        {
            ViewBag.DocumentId = documentId;
            ViewBag.VersionId = versionId;
            return View();
        }

        // POST: /Home/Ask
        [HttpPost]
        public async Task<IActionResult> Ask(string documentId, string versionId, string query, int topK = 3)
        {
            try
            {
                _logger.LogInformation($"Processing question: {query}");

                // 1. Pozovi Retrieval Service za relevantne chunk-ove
                var searchRequest = new SearchRequest
                {
                    Query = query,
                    DocumentId = documentId,
                    VersionId = versionId,
                    TopK = topK
                };

                var searchJson = JsonSerializer.Serialize(searchRequest);
                var searchContent = new StringContent(searchJson, Encoding.UTF8, "application/json");

                var searchResponse = await _httpClient.PostAsync(RetrievalServiceUrl, searchContent);

                if (!searchResponse.IsSuccessStatusCode)
                {
                    ViewBag.Error = "Failed to retrieve relevant context";
                    ViewBag.DocumentId = documentId;
                    ViewBag.VersionId = versionId;
                    return View();
                }

                var searchResultJson = await searchResponse.Content.ReadAsStringAsync();
                var searchResult = JsonSerializer.Deserialize<SearchResponse>(searchResultJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation($"Retrieved {searchResult.Results.Count} relevant chunks");

                // 2. Pripremi context za LLM
                var contextChunks = searchResult.Results
                    .Select(r => r.Text)
                    .ToList();

                // 3. Pozovi LLM Service
                var llmRequest = new LLMRequest
                {
                    Query = query,
                    Context = contextChunks,
                    SystemPrompt = @"Ti si precizan AI asistent za srpske pravne dokumente.
                    KLJUÈNO PRAVILO: Kad korisnik pita 'Šta je X?' ili 'Šta su X?', 
                    pronaði DEFINICIJU iz Èlana 2 ili drugih èlanova.
                    Odgovaraj samo na osnovu konteksta. Ne izmišljaj."
                };

                var llmJson = JsonSerializer.Serialize(llmRequest);
                var llmContent = new StringContent(llmJson, Encoding.UTF8, "application/json");

                var llmResponse = await _httpClient.PostAsync($"{LLMServiceUrl}/generate", llmContent);

                if (!llmResponse.IsSuccessStatusCode)
                {
                    var errorContent = await llmResponse.Content.ReadAsStringAsync();
                    ViewBag.Error = $"LLM failed: {errorContent}";
                    ViewBag.DocumentId = documentId;
                    ViewBag.VersionId = versionId;
                    return View();
                }

                var llmResultJson = await llmResponse.Content.ReadAsStringAsync();
                var llmResult = JsonSerializer.Deserialize<LLMResponse>(llmResultJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation($"LLM generated answer: {llmResult.Answer.Length} chars");

                // 4. Vrati rezultat
                ViewBag.DocumentId = documentId;
                ViewBag.VersionId = versionId;
                ViewBag.Query = query;
                ViewBag.Answer = llmResult.Answer;
                ViewBag.Model = llmResult.Model;
                ViewBag.TokensUsed = llmResult.TokensUsed;
                ViewBag.RetrievedChunks = searchResult.Results;
                ViewBag.Citations = llmResult.Citations;
                ViewBag.HasSufficientContext = llmResult.HasSufficientContext;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing question");
                ViewBag.Error = $"Error: {ex.Message}";
                ViewBag.DocumentId = documentId;
                ViewBag.VersionId = versionId;
                return View();
            }
        }
        #endregion

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
                _logger.LogInformation($"Parsing version {versionId} for document {documentId}");

                var url = $"{DocumentServiceUrl}/versions/{versionId}/parse";
                _logger.LogInformation($"Calling: {url}");

                var response = await _httpClient.PostAsync(url, null);

                _logger.LogInformation($"Response status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Parse failed: {errorContent}");
                    TempData["Error"] = $"Parse failed: {errorContent}";
                }
                else
                {
                    var successContent = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"Parse success: {successContent}");
                    TempData["Success"] = "Document parsed successfully!";
                }

                return RedirectToAction("Versions", new { documentId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Parse error");
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