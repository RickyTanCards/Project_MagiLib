using Microsoft.JSInterop;

namespace MagicKeywords.Web.Services;

/// <summary>Favorites kept in the browser's localStorage (iOS can evict it if the app goes unused, so export/import is offered).</summary>
public class Favorites
{
    const string Key = "mk.favorites";
    readonly IJSRuntime _js;
    readonly HashSet<string> _set = new(StringComparer.OrdinalIgnoreCase);

    public Favorites(IJSRuntime js) => _js = js;

    public event Action? Changed;
    public IReadOnlyCollection<string> Names => _set;
    public bool Is(Keyword k) => _set.Contains(k.Name);

    public async Task InitAsync()
    {
        var raw = await _js.InvokeAsync<string?>("mk.get", Key);
        if (!string.IsNullOrWhiteSpace(raw)) Load(raw.Split('\n'));
    }

    void Load(IEnumerable<string> names)
    {
        foreach (var n in names.Select(x => x.Trim()).Where(x => x.Length > 0))
            _set.Add(n == "Totem Armor" ? "Umbra Armor" : n); // renamed in the rules
    }

    public async Task ToggleAsync(Keyword k)
    {
        if (!_set.Remove(k.Name)) _set.Add(k.Name);
        await SaveAsync();
    }

    public string Export() => string.Join("\n", _set.OrderBy(x => x));

    public async Task ImportAsync(string text)
    {
        Load(text.Split('\n'));
        await SaveAsync();
    }

    async Task SaveAsync()
    {
        await _js.InvokeVoidAsync("mk.set", Key, Export());
        Changed?.Invoke();
    }
}
