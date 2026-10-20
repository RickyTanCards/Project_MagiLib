using System.ComponentModel;
using System.Text.Json;

namespace MagicKeywords;

public record Example(string Card, string Text)
{
    public string Heading => string.IsNullOrEmpty(Card) ? "Scenario" : Card;
}

public class Keyword : INotifyPropertyChanged
{
    bool _favorite;

    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Definition { get; init; } = "";
    public List<Example> Examples { get; init; } = new();
    public string Letter => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "#";

    public bool IsFavorite
    {
        get => _favorite;
        set
        {
            if (_favorite == value) return;
            _favorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Star)));
        }
    }

    public string Star => _favorite ? "★" : "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static List<Keyword> LoadAll()
    {
        var asm = typeof(Keyword).Assembly;
        var all = new List<Keyword>();
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.EndsWith(".json")))
        {
            using var s = asm.GetManifestResourceStream(res)!;
            var rows = JsonSerializer.Deserialize<List<JsonElement>>(s)!;
            foreach (var r in rows)
            {
                all.Add(new Keyword
                {
                    Name = r[0].GetString()!,
                    Kind = r[1].GetString()!,
                    Definition = r[2].GetString()!,
                    Examples = r[3].EnumerateArray()
                        .Select(e => new Example(e[0].GetString()!, e[1].GetString()!)).ToList(),
                });
            }
        }
        return all.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
