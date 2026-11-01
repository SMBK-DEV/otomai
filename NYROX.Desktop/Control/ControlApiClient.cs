using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NYROX.Desktop.Control;

internal sealed class ControlApiClient : IDisposable
{
    private readonly HttpClient _http;

    public ControlApiClient(string token)
    {
        _http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:48621/") };
        _http.DefaultRequestHeaders.Add("X-NYROX-Token", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.Timeout = TimeSpan.FromSeconds(8);
    }

    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<T>(path, cancellationToken);

    public async Task<HttpResponseMessage> PostAsync<T>(string path, T body, CancellationToken cancellationToken = default) =>
        await _http.PostAsJsonAsync(path, body, cancellationToken);

    public async Task<HttpResponseMessage> PostEmptyAsync(string path, CancellationToken cancellationToken = default) =>
        await _http.PostAsync(path, null, cancellationToken);

    public async Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        await _http.DeleteAsync(path, cancellationToken);

    public void Dispose() => _http.Dispose();
}
