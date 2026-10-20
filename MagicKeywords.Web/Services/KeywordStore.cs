namespace MagicKeywords.Web.Services;

public class KeywordStore
{
    public List<Keyword> All { get; } = Keyword.LoadAll();

    public Keyword? Find(string name) =>
        All.FirstOrDefault(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string Ref(Keyword k, out string text)
    {
        // Definitions end with " [CR 702.x]"; split that off so the UI can show it separately.
        var d = k.Definition;
        int i = d.LastIndexOf(" [CR ", StringComparison.Ordinal);
        if (i >= 0 && d.EndsWith(']')) { text = d[(i + 2)..^1]; return d[..i]; }
        text = "";
        return d;
    }

    public static string Link(Keyword k) => "keyword/" + Uri.EscapeDataString(k.Name);
}
