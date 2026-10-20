using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MagicKeywords;

public partial class CombatView : UserControl
{
    readonly CreatureEditor _atk = new("CREATURE 1 — ATTACKER (Player 1, active player)", "Creature 1", 2, 2);
    readonly CreatureEditor _def = new("CREATURE 2 — POTENTIAL BLOCKER (Player 2)", "Creature 2", 2, 2);
    readonly CheckBox _blocks = Opt("Creature 2 blocks Creature 1 if it legally can", true);
    readonly CheckBox _sick = Opt("Creature 1 is summoning sick (it came under its controller's control this turn)", false);
    readonly CheckBox _tapped = Opt("Creature 2 starts the combat tapped", false);
    readonly CheckBox _land = Opt("Player 2 controls a land matching Creature 1's landwalk type", true);
    readonly TextBox _life1 = LifeBox(), _life2 = LifeBox();

    static CheckBox Opt(string text, bool on) => new()
    {
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        IsChecked = on, Margin = new Thickness(0, 3, 0, 3),
    };

    static TextBox LifeBox() => new()
    {
        Text = "20", Width = 50, Height = 26, TextAlignment = TextAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public CombatView()
    {
        InitializeComponent();
        Left.Children.Add(_atk);
        Left.Children.Add(_def);

        var opts = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        opts.Children.Add(Header("SITUATION"));
        opts.Children.Add(_blocks);
        opts.Children.Add(_sick);
        opts.Children.Add(_tapped);
        opts.Children.Add(_land);
        var lives = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        lives.Children.Add(new TextBlock { Text = "Player 1 life", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        lives.Children.Add(_life1);
        lives.Children.Add(new TextBlock { Text = "Player 2 life", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 8, 0) });
        lives.Children.Add(_life2);
        opts.Children.Add(lives);
        Left.Children.Add(opts);

        ShowIntro();
    }

    static TextBlock Header(string t) => new()
    {
        Text = t, FontWeight = FontWeights.Bold, FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)), Margin = new Thickness(0, 0, 0, 6),
    };

    void SwapClicked(object s, RoutedEventArgs e)
    {
        var a = _atk.ToSpec(); var d = _def.ToSpec();
        _atk.Load(d);
        _def.Load(a);
    }

    void RunClicked(object s, RoutedEventArgs e)
    {
        var o = new SimOptions
        {
            DefenderBlocks = _blocks.IsChecked == true,
            AttackerSick = _sick.IsChecked == true,
            DefenderTapped = _tapped.IsChecked == true,
            DefenderControlsLand = _land.IsChecked == true,
            AttackerLife = ParseInt(_life1.Text, 20),
            DefenderLife = ParseInt(_life2.Text, 20),
        };
        Render(CombatSim.Simulate(_atk.ToSpec(), _def.ToSpec(), o));
    }

    internal static int ParseInt(string? s, int fallback) => int.TryParse(s?.Trim(), out var v) ? v : fallback;

    void ShowIntro()
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Segoe UI") };
        doc.Blocks.Add(new Paragraph(new Run("Build two creatures, set their power, toughness and keywords, then press SIMULATE COMBAT. " +
            "Creature 1 attacks alone and Creature 2 may block. The log walks through each step, shows every triggered ability as it goes onto the stack " +
            "and resolves, and explains the outcome with rule numbers from the Comprehensive Rules.")) { Foreground = Brushes.Gray });
        Out.Document = doc;
    }

    void Render(List<LogLine> lines)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily("Segoe UI") };
        doc.Blocks.Add(Legend());
        foreach (var l in lines)
        {
            var p = new Paragraph { Margin = new Thickness(l.Indent * 22, 2, 0, 2), LineHeight = 20 };
            switch (l.Kind)
            {
                case LogKind.Phase:
                    p.Inlines.Add(new Run(l.Text.ToUpper()) { FontWeight = FontWeights.Black, FontSize = 15 });
                    p.Margin = new Thickness(0, 20, 0, 6);
                    p.BorderBrush = Brushes.Black; p.BorderThickness = new Thickness(0, 0, 0, 2); p.Padding = new Thickness(0, 0, 0, 3);
                    break;
                case LogKind.Info:
                    p.Inlines.Add(new Run(l.Text));
                    break;
                case LogKind.Rule:
                    p.Inlines.Add(new Run(l.Text) { Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)), FontStyle = FontStyles.Italic, FontSize = 13 });
                    break;
                case LogKind.Trigger:
                    p.Inlines.Add(new Run("⚡ TRIGGER  ") { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x6A, 0x00)) });
                    p.Inlines.Add(new Run(l.Text));
                    break;
                case LogKind.Stack:
                    p.Inlines.Add(new Run(l.Text) { FontFamily = new FontFamily("Consolas"), FontSize = 13 });
                    p.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
                    p.Padding = new Thickness(8, 1, 8, 1);
                    if (l.Indent == 0) p.Inlines.InsertBefore(p.Inlines.FirstInline, new Run("▤ ") { FontWeight = FontWeights.Bold });
                    break;
                case LogKind.Resolve:
                    p.Inlines.Add(new Run("✔ " + l.Text) { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x7A, 0x3A)) });
                    p.Margin = new Thickness(0, 8, 0, 2);
                    break;
                case LogKind.Damage:
                    p.Inlines.Add(new Run(l.Text) { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E)) });
                    break;
                case LogKind.Result:
                    p.Inlines.Add(new Run(l.Text) { FontWeight = FontWeights.Bold, FontSize = 15 });
                    p.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
                    p.Padding = new Thickness(10, 4, 10, 4);
                    break;
            }
            doc.Blocks.Add(p);
        }
        Out.Document = doc;
        Out.ScrollToHome();
    }

    static Paragraph Legend()
    {
        var p = new Paragraph { FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        p.Inlines.Add(new Run("⚡ trigger  ") { Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x6A, 0x00)), FontWeight = FontWeights.Bold });
        p.Inlines.Add(new Run("▤ stack  ") { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x2C, 0x5A, 0xA0)) });
        p.Inlines.Add(new Run("✔ resolves  ") { Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x7A, 0x3A)), FontWeight = FontWeights.Bold });
        p.Inlines.Add(new Run("damage  ") { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x1E, 0x1E)) });
        p.Inlines.Add(new Run("rule note") { Foreground = Brushes.Gray, FontStyle = FontStyles.Italic });
        return p;
    }
}

/// <summary>Editor for one pseudo creature: name, P/T, colors, and keyword checkboxes.</summary>
public class CreatureEditor : StackPanel
{
    readonly TextBox _name = new() { Height = 28, VerticalContentAlignment = VerticalAlignment.Center, Width = 180 };
    readonly TextBox _pow = PtBox(), _tou = PtBox();
    readonly Dictionary<char, CheckBox> _colors = new();
    readonly CheckBox _artifact = new() { Content = "Artifact", Margin = new Thickness(0, 0, 12, 0) };
    readonly Dictionary<string, (CheckBox check, TextBox? n)> _kw = new(StringComparer.OrdinalIgnoreCase);

    static TextBox PtBox() => new()
    {
        Width = 44, Height = 28, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
    };

    public CreatureEditor(string title, string defaultName, int p, int t)
    {
        Margin = new Thickness(0, 0, 0, 18);
        Children.Add(new Border
        {
            BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 0, 0, 2), Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(0, 0, 0, 4),
            Child = new TextBlock { Text = title, FontWeight = FontWeights.Black, FontSize = 13 },
        });

        _name.Text = defaultName; _pow.Text = p.ToString(); _tou.Text = t.ToString();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(_name);
        row.Children.Add(new TextBlock { Text = "  Power ", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_pow);
        row.Children.Add(new TextBlock { Text = " / Toughness ", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(_tou);
        Children.Add(row);

        var colors = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (c, n) in new[] { ('W', "White"), ('U', "Blue"), ('B', "Black"), ('R', "Red"), ('G', "Green") })
        {
            var cb = new CheckBox { Content = n, Margin = new Thickness(0, 0, 12, 0) };
            _colors[c] = cb; colors.Children.Add(cb);
        }
        colors.Children.Add(_artifact);
        Children.Add(colors);

        foreach (var g in SimKeywords.All.GroupBy(k => k.Group))
        {
            Children.Add(new TextBlock
            {
                Text = g.Key.ToUpper(), FontSize = 11, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B)), Margin = new Thickness(0, 6, 0, 3),
            });
            var wrap = new WrapPanel();
            foreach (var k in g)
            {
                var cb = new CheckBox { Content = k.Name, VerticalAlignment = VerticalAlignment.Center };
                TextBox? n = null;
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 14, 2) };
                item.Children.Add(cb);
                if (k.Numeric)
                {
                    n = new TextBox { Text = "1", Width = 26, Height = 20, FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                    item.Children.Add(n);
                }
                wrap.Children.Add(item);
                _kw[k.Name] = (cb, n);
            }
            Children.Add(wrap);
        }
    }

    public CreatureSpec ToSpec()
    {
        var s = new CreatureSpec
        {
            Name = _name.Text,
            Power = CombatView.ParseInt(_pow.Text, 0),
            Toughness = CombatView.ParseInt(_tou.Text, 1),
            Artifact = _artifact.IsChecked == true,
        };
        foreach (var (c, cb) in _colors) if (cb.IsChecked == true) s.Colors.Add(c);
        foreach (var (name, (cb, n)) in _kw)
            if (cb.IsChecked == true) s.Keywords[name] = n == null ? 1 : Math.Max(1, CombatView.ParseInt(n.Text, 1));
        return s;
    }

    public void Load(CreatureSpec s)
    {
        _name.Text = s.Name; _pow.Text = s.Power.ToString(); _tou.Text = s.Toughness.ToString();
        _artifact.IsChecked = s.Artifact;
        foreach (var (c, cb) in _colors) cb.IsChecked = s.Colors.Contains(c);
        foreach (var (name, (cb, n)) in _kw)
        {
            cb.IsChecked = s.Keywords.ContainsKey(name);
            if (n != null) n.Text = s.Keywords.TryGetValue(name, out var v) ? v.ToString() : "1";
        }
    }
}
