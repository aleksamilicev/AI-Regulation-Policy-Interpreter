using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using System;
using System.Collections.Generic;
using System.Fabric;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    public class Client : StatelessService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        public Client(StatelessServiceContext context) : base(context) { }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new[] {
                new ServiceInstanceListener(context => new FormListener(context, _httpClient))
            };
        }
    }

    public class FormListener : ICommunicationListener
    {
        private readonly StatelessServiceContext _context;
        private readonly HttpClient _httpClient;
        private HttpListener _listener;

        public FormListener(StatelessServiceContext context, HttpClient httpClient)
        {
            _context = context;
            _httpClient = httpClient;
        }

        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            var endpoint = _context.CodePackageActivationContext.GetEndpoint("ClientEndpoint");
            string url = $"http://+:{endpoint.Port}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(url);
            _listener.Start();

            Task.Run(() => HandleRequests(cancellationToken));
            return Task.FromResult(url.Replace("+", "localhost"));
        }

        private async Task<string> ExecuteRollback()
        {
            try
            {
                await _httpClient.PostAsync("http://localhost:8081/rollback", null);
                await _httpClient.PostAsync("http://localhost:8082/rollback", null);

                return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body { font-family: Arial; margin: 40px; background: #f5f5f5; }
        .container { max-width: 600px; margin: auto; background: white; padding: 30px; border-radius: 8px; text-align: center; }
        .success { background: #d4edda; color: #155724; padding: 20px; border-radius: 5px; margin: 20px 0; border: 1px solid #c3e6cb; }
        a { color: #007bff; text-decoration: none; display: inline-block; margin: 10px; padding: 10px 20px; background: #007bff; color: white; border-radius: 5px; }
        a:hover { background: #0056b3; text-decoration: none; }
    </style>
</head>
<body>
    <div class='container'>
        <h2>Rollback Uspešan</h2>
        <div class='success'>
            <strong>Sva stanja su vraćena na prethodno stanje!</strong><br><br>
            Bookstore i Bank su resetovani na prethodne vrednosti.
        </div>
        <a href='/books'>Proveri Knjige</a>
        <a href='/clients'>Proveri Klijente</a>
        <br><br>
        <a href='/' style='background: #6c757d;'>← Nazad na početnu</a>
    </div>
</body>
</html>";
            }
            catch (Exception ex)
            {
                return $"<html><body><h2>Greška pri rollback-u: {ex.Message}</h2><a href='/'>Nazad</a></body></html>";
            }
        }

        private async Task HandleRequests(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var ctx = await _listener.GetContextAsync();
                string path = ctx.Request.Url.AbsolutePath;

                if (ctx.Request.HttpMethod == "GET")
                {
                    string html = "";

                    if (path == "/")
                    {
                        html = GetMainPage();
                    }
                    else if (path == "/books")
                    {
                        html = await GetBooksPage();
                    }
                    else if (path == "/clients")
                    {
                        html = await GetClientsPage();
                    }
                    else if (path == "/order")
                    {
                        html = GetOrderPage();
                    }
                    else if (path == "/rollback")
                    {
                        html = await ExecuteRollback();
                    }

                    var buffer = Encoding.UTF8.GetBytes(html);
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else if (ctx.Request.HttpMethod == "POST" && path == "/order")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    var formData = await reader.ReadToEndAsync();
                    var result = await ProcessOrder(formData);

                    var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial; margin: 40px; background: #f5f5f5; }}
        .container {{ max-width: 600px; margin: auto; background: white; padding: 30px; border-radius: 8px; }}
        .result {{ padding: 20px; border-radius: 5px; margin: 20px 0; }}
        .success {{ background: #d4edda; color: #155724; border: 1px solid #c3e6cb; }}
        .error {{ background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }}
        a {{ color: #007bff; text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <div class='container'>
        <h2>Rezultat Narudžbine</h2>
        <div class='result {(result.Contains("Uspešna") ? "success" : "error")}'>
            {result}
        </div>
        <a href='/'>← Nazad na početnu</a>
    </div>
</body>
</html>";

                    var buffer = Encoding.UTF8.GetBytes(html);
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    await ctx.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }

                ctx.Response.Close();
            }
        }

        private string GetMainPage()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body { font-family: Arial; margin: 40px; background: #f5f5f5; }
        .container { max-width: 800px; margin: auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        h1 { color: #333; text-align: center; }
        .menu { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-top: 30px; }
        .menu-item { padding: 30px; background: #007bff; color: white; text-align: center; border-radius: 8px; text-decoration: none; display: block; transition: background 0.3s; }
        .menu-item:hover { background: #0056b3; }
        .menu-item h3 { margin: 0; }
        .menu-item p { margin: 10px 0 0 0; font-size: 14px; opacity: 0.9; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>📚 Bookstore Management System</h1>
        <div class='menu'>
            <a href='/books' class='menu-item'>
                <h3>📖 Dostupne Knjige</h3>
                <p>Pregledaj katalog knjiga</p>
            </a>
            <a href='/clients' class='menu-item'>
                <h3>👥 Klijenti Banke</h3>
                <p>Pregledaj klijente i stanja</p>
            </a>
            <a href='/order' class='menu-item' style='grid-column: 1 / -1; background: #28a745;'>
                <h3>🛒 Naruči Knjigu</h3>
                <p>Kreiraj novu narudžbinu</p>
            </a>
            <a href='/rollback' class='menu-item' style='grid-column: 1 / -1; background: #dc3545;'>
                <h3>🔄 Rollback</h3>
                <p>Vrati sve na početno stanje</p>
            </a>
        </div>
    </div>
</body>
</html>";
        }

        private async Task<string> GetBooksPage()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost:8081/list");
                var json = await response.Content.ReadAsStringAsync();
                var books = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);

                var rows = "";
                foreach (var book in books)
                {
                    rows += $@"
                    <tr>
                        <td>{book["ID"]}</td>
                        <td>{book["Title"]}</td>
                        <td>{book["Price"]} RSD</td>
                        <td>{book["Stock"]}</td>
                    </tr>";
                }

                return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial; margin: 40px; background: #f5f5f5; }}
        .container {{ max-width: 800px; margin: auto; background: white; padding: 30px; border-radius: 8px; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 20px; }}
        th, td {{ padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }}
        th {{ background: #007bff; color: white; }}
        tr:hover {{ background: #f5f5f5; }}
        a {{ color: #007bff; text-decoration: none; }}
    </style>
</head>
<body>
    <div class='container'>
        <h2>📖 Dostupne Knjige</h2>
        <table>
            <tr>
                <th>ID</th>
                <th>Naziv</th>
                <th>Cena</th>
                <th>Na stanju</th>
            </tr>
            {rows}
        </table>
        <br>
        <a href='/'>← Nazad</a>
    </div>
</body>
</html>";
            }
            catch
            {
                return "<html><body><h2>Greška pri učitavanju knjiga</h2><a href='/'>Nazad</a></body></html>";
            }
        }

        private async Task<string> GetClientsPage()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost:8082/list");
                var json = await response.Content.ReadAsStringAsync();
                var clients = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);

                var rows = "";
                foreach (var client in clients)
                {
                    rows += $@"
                    <tr>
                        <td>{client["UserID"]}</td>
                        <td>{client["Name"]}</td>
                        <td>{client["Balance"]} RSD</td>
                    </tr>";
                }

                return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: Arial; margin: 40px; background: #f5f5f5; }}
        .container {{ max-width: 800px; margin: auto; background: white; padding: 30px; border-radius: 8px; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 20px; }}
        th, td {{ padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }}
        th {{ background: #007bff; color: white; }}
        tr:hover {{ background: #f5f5f5; }}
        a {{ color: #007bff; text-decoration: none; }}
    </style>
</head>
<body>
    <div class='container'>
        <h2>👥 Klijenti Banke</h2>
        <table>
            <tr>
                <th>User ID</th>
                <th>Ime</th>
                <th>Stanje</th>
            </tr>
            {rows}
        </table>
        <br>
        <a href='/'>← Nazad</a>
    </div>
</body>
</html>";
            }
            catch
            {
                return "<html><body><h2>Greška pri učitavanju klijenata</h2><a href='/'>Nazad</a></body></html>";
            }
        }

        private string GetOrderPage()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body { font-family: Arial; margin: 40px; background: #f5f5f5; }
        .container { max-width: 600px; margin: auto; background: white; padding: 30px; border-radius: 8px; }
        input, select { width: 100%; padding: 10px; margin: 10px 0; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        label { font-weight: bold; color: #555; }
        button { background: #28a745; color: white; padding: 12px 30px; border: none; border-radius: 4px; cursor: pointer; font-size: 16px; width: 100%; margin-top: 10px; }
        button:hover { background: #218838; }
        a { color: #007bff; text-decoration: none; }
        .info { background: #e7f3ff; padding: 15px; border-left: 4px solid #007bff; margin: 20px 0; }
    </style>
</head>
<body>
    <div class='container'>
        <h2>🛒 Nova Narudžbina</h2>
        <div class='info'>
            <strong>Napomena:</strong> ID knjige možete videti na strani <a href='/books'>Dostupne Knjige</a>
        </div>
        <form method='post'>
            <label>User ID (U1, U2, U3):</label>
            <input type='text' name='userID' value='U1' required>
            
            <label>Book ID (B1, B2, B3):</label>
            <input type='text' name='bookID' value='B1' required>
            
            <label>Količina:</label>
            <input type='number' name='quantity' value='1' min='1' required>
            
            <button type='submit'>Naruči</button>
        </form>
        <br>
        <a href='/'>← Nazad na početnu</a>
    </div>
</body>
</html>";
        }

        private string GetRollbackTestPage()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body { font-family: Arial; margin: 40px; background: #f5f5f5; }
        .container { max-width: 600px; margin: auto; background: white; padding: 30px; border-radius: 8px; }
        input, select { width: 100%; padding: 10px; margin: 10px 0; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        label { font-weight: bold; color: #555; }
        button { background: #ffc107; color: #333; padding: 12px 30px; border: none; border-radius: 4px; cursor: pointer; font-size: 16px; width: 100%; margin-top: 10px; }
        button:hover { background: #e0a800; }
        a { color: #007bff; text-decoration: none; }
        .warning { background: #fff3cd; padding: 15px; border-left: 4px solid #ffc107; margin: 20px 0; color: #856404; }
        .test-option { background: #f8f9fa; padding: 15px; margin: 10px 0; border-radius: 5px; border: 2px solid #dee2e6; cursor: pointer; }
        .test-option:hover { border-color: #ffc107; }
        input[type=radio] { width: auto; margin-right: 10px; }
    </style>
</head>
<body>
    <div class='container'>
        <h2>🔄 Test Rollback Mehanizma</h2>
        <div class='warning'>
            <strong>⚠️ Test Scenario:</strong> Izaberi test koji će namerno izazvati grešku i proveriti da li se stanje vraća na prethodno (rollback).
        </div>
        <form method='post'>
            <label>Izaberi Test Scenario:</label>
            
            <div class='test-option'>
                <input type='radio' name='testType' value='insufficient_funds' checked>
                <strong>Test 1: Nedovoljno Sredstava</strong><br>
                <small>Pokušaj da kupiš skupu knjigu sa malo para → Očekivani rezultat: Greška, stanje nepromenjeno</small>
            </div>
            
            <div class='test-option'>
                <input type='radio' name='testType' value='insufficient_stock'>
                <strong>Test 2: Nedovoljno Knjiga</strong><br>
                <small>Pokušaj da kupiš više knjiga nego što ima → Očekivani rezultat: Greška, stanje nepromenjeno</small>
            </div>
            
            <div class='test-option'>
                <input type='radio' name='testType' value='invalid_book'>
                <strong>Test 3: Nepostojeća Knjiga</strong><br>
                <small>Pokušaj da kupiš knjigu koja ne postoji → Očekivani rezultat: Greška, stanje nepromenjeno</small>
            </div>
            
            <button type='submit'>▶️ Pokreni Test</button>
        </form>
        <br>
        <a href='/'>← Nazad na početnu</a>
    </div>
</body>
</html>";
        }

        private async Task<string> TestRollback(string formData)
        {
            try
            {
                var testType = "";
                foreach (var pair in formData.Split('&'))
                {
                    var kv = pair.Split('=');
                    if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == "testType")
                        testType = Uri.UnescapeDataString(kv[1]);
                }

                // Prvo preuzmi trenutna stanja
                var booksRes = await _httpClient.GetAsync("http://localhost:8081/list");
                var clientsRes = await _httpClient.GetAsync("http://localhost:8082/list");
                var booksBefore = await booksRes.Content.ReadAsStringAsync();
                var clientsBefore = await clientsRes.Content.ReadAsStringAsync();

                // Izaberi test scenario
                object request = null;
                string description = "";

                if (testType == "insufficient_funds")
                {
                    request = new
                    {
                        order = new { Title = "B2", Quantity = 10 }, // 10 x 2000 = 20000 RSD
                        userID = "U2" // Ima samo 3000 RSD
                    };
                    description = "Test: Pokušaj kupovine knjige B2 (10x2000 RSD) sa računom koji ima samo 3000 RSD";
                }
                else if (testType == "insufficient_stock")
                {
                    request = new
                    {
                        order = new { Title = "B1", Quantity = 100 }, // Traži 100 komada
                        userID = "U1" // Knjiga ima samo ~10 komada
                    };
                    description = "Test: Pokušaj kupovine 100 komada knjige B1 koja ima samo 10 na stanju";
                }
                else if (testType == "invalid_book")
                {
                    request = new
                    {
                        order = new { Title = "B999", Quantity = 1 },
                        userID = "U1"
                    };
                    description = "Test: Pokušaj kupovine nepostojeće knjige B999";
                }

                // Izvrši transakciju (očekuje se greška)
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("http://localhost:8083/process", content);
                var resultJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);

                // Proveri stanja posle
                var booksResAfter = await _httpClient.GetAsync("http://localhost:8081/list");
                var clientsResAfter = await _httpClient.GetAsync("http://localhost:8082/list");
                var booksAfter = await booksResAfter.Content.ReadAsStringAsync();
                var clientsAfter = await clientsResAfter.Content.ReadAsStringAsync();

                // Uporedi
                bool rollbackSuccessful = (booksBefore == booksAfter) && (clientsBefore == clientsAfter);

                return $@"
<strong>{description}</strong><br><br>
<strong>📋 Rezultat transakcije:</strong> {result["message"]}<br><br>
<strong>🔍 Rollback provera:</strong><br>
- Stanje knjiga pre: {(booksBefore.Length > 100 ? "OK" : "ERROR")}<br>
- Stanje knjiga posle: {(booksAfter.Length > 100 ? "OK" : "ERROR")}<br>
- Stanje klijenata pre: {(clientsBefore.Length > 50 ? "OK" : "ERROR")}<br>
- Stanje klijenata posle: {(clientsAfter.Length > 50 ? "OK" : "ERROR")}<br><br>
<strong style='color: {(rollbackSuccessful ? "green" : "red")}'>
    {(rollbackSuccessful ? "✅ ROLLBACK USPEŠAN - Stanje je vraćeno!" : "❌ ROLLBACK NEUSPEŠAN - Stanje je promenjeno!")}
</strong><br><br>
<em>Proveri ručno stranicu /books i /clients da potvrdiš.</em>";
            }
            catch (Exception ex)
            {
                return $"Greška pri testiranju: {ex.Message}";
            }
        }

        private async Task<string> ProcessOrder(string formData)
        {
            try
            {
                var formDict = new Dictionary<string, string>();
                foreach (var pair in formData.Split('&'))
                {
                    var kv = pair.Split('=');
                    if (kv.Length == 2)
                        formDict[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
                }

                var request = new
                {
                    order = new
                    {
                        Title = formDict["bookID"],
                        Quantity = int.Parse(formDict["quantity"])
                    },
                    userID = formDict["userID"]
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("http://localhost:8083/process", content);
                var resultJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);

                return result["message"].ToString();
            }
            catch (Exception ex)
            {
                return $"Greška pri obradi narudžbine: {ex.Message}";
            }
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            return Task.CompletedTask;
        }

        public void Abort() => _listener?.Abort();
    }
}