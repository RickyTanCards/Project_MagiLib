using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MagicKeywords;

public partial class MainWindow : Window
{
    readonly List<Keyword> _all = Keyword.LoadAll();
    readonly string _favPath = ResolveFavoritesPath();
    bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        LoadFavorites();
        _ready = true;
        Refresh();
    }

    // Favorites live next to the exe so they travel with a USB drive; fall back to AppData if read-only.
    static string ResolveFavoritesPath()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, "favorites.txt");
        try
        {
            using var _ = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            return path;
        }
        catch
        {
            var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MagicKeywords");
            Directory.CreateDirectory(fallback);
            return Path.Combine(fallback, "favorites.txt");
        }
    }

    void LoadFavorites()
    {
        if (!File.Exists(_favPath)) return;
        var names = new HashSet<string>(File.ReadAllLines(_favPath), StringComparer.OrdinalIgnoreCase);
        // Totem Armor was renamed Umbra Armor in the rules; keep old favorites.
        if (names.Contains("Totem Armor")) names.Add("Umbra Armor");
        foreach (var k in _all) k.IsFavorite = names.Contains(k.Name);
    }

    void SaveFavorites()
    {
        try { File.WriteAllLines(_favPath, _all.Where(k => k.IsFavorite).Select(k => k.Name)); }
        catch (Exception ex) { MessageBox.Show("Could not save favorites: " + ex.Message); }
    }

    void Refresh()
    {
        if (!_ready) return;
        var text = SearchBox.Text.Trim();
        var kind = (KindFilter.SelectedItem as ComboBoxItem)?.Content as string ?? "All";
        IEnumerable<Keyword> q = _all;

        if (kind != "All") q = q.Where(k => k.Kind == kind);
        if (ModeFavorites.IsChecked == true) q = q.Where(k => k.IsFavorite);

        if (ModeSearch.IsChecked == true)
        {
            // Search mode: name matches first, then matches inside the definition.
            if (text.Length == 0) q = Enumerable.Empty<Keyword>();
            else q = q.Where(k => k.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                               || k.Definition.Contains(text, StringComparison.OrdinalIgnoreCase))
                      .OrderByDescending(k => k.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                      .ThenByDescending(k => k.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                      .ThenBy(k => k.Name);
        }
        else if (text.Length > 0)
        {
            q = q.Where(k => k.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.ToList();
        var selected = ResultList.SelectedItem as Keyword;

        var view = new ListCollectionView(list);
        if (ModeSearch.IsChecked != true) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Keyword.Letter)));
        ResultList.ItemsSource = view;

        CountText.Text = ModeSearch.IsChecked == true && text.Length == 0
            ? "Type to search keywords and definitions."
            : $"{list.Count} result{(list.Count == 1 ? "" : "s")}";

        if (selected != null && list.Contains(selected)) ResultList.SelectedItem = selected;
        else if (list.Count == 1) ResultList.SelectedIndex = 0;
        else ShowDetail(null);
    }

    void ShowDetail(Keyword? k)
    {
        DetailPanel.Visibility = k == null ? Visibility.Collapsed : Visibility.Visible;
        Placeholder.Visibility = k == null ? Visibility.Visible : Visibility.Collapsed;
        if (k == null) return;
        NameText.Text = k.Name;
        KindText.Text = k.Kind;
        DefText.Text = k.Definition;
        ExampleList.ItemsSource = k.Examples;
        UpdateFavButton(k);
    }

    void UpdateFavButton(Keyword k)
    {
        FavButton.IsChecked = k.IsFavorite;
        FavButton.Content = k.IsFavorite ? "★ FAVORITED" : "☆ FAVORITE";
    }

    void ModeChanged(object s, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool sim = ModeSim.IsChecked == true;
        SimView.Visibility = sim ? Visibility.Visible : Visibility.Collapsed;
        GuideView.Visibility = sim ? Visibility.Collapsed : Visibility.Visible;
        if (!sim) Refresh();
    }
    void FilterChanged(object s, EventArgs e) => Refresh();
    void SelectionChanged(object s, SelectionChangedEventArgs e) => ShowDetail(ResultList.SelectedItem as Keyword);

    void FavClicked(object s, RoutedEventArgs e)
    {
        if (ResultList.SelectedItem is not Keyword k) return;
        k.IsFavorite = !k.IsFavorite;
        SaveFavorites();
        UpdateFavButton(k);
        if (ModeFavorites.IsChecked == true && !k.IsFavorite) Refresh();
    }
}
