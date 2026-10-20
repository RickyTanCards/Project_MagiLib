namespace MagicKeywords.Web.Services;

public class CreatureModel
{
    public string Name = "";
    public int Power = 2, Toughness = 2;
    public HashSet<char> Colors = new();
    public bool Artifact;
    public Dictionary<string, int> Kw = new(StringComparer.OrdinalIgnoreCase);

    public CreatureSpec ToSpec() => new()
    {
        Name = Name, Power = Power, Toughness = Toughness, Artifact = Artifact,
        Colors = new HashSet<char>(Colors),
        Keywords = new Dictionary<string, int>(Kw, StringComparer.OrdinalIgnoreCase),
    };

    public CreatureModel Clone() => new()
    {
        Name = Name, Power = Power, Toughness = Toughness, Artifact = Artifact,
        Colors = new HashSet<char>(Colors), Kw = new Dictionary<string, int>(Kw, StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>Simulator inputs and last result, kept alive while the user moves between tabs.</summary>
public class SimState
{
    public CreatureModel Atk = new() { Name = "Creature 1" };
    public CreatureModel Def = new() { Name = "Creature 2" };
    public bool DefenderBlocks = true, AttackerSick, DefenderTapped, DefenderControlsLand = true;
    public int AttackerLife = 20, DefenderLife = 20;
    public List<LogLine>? Result;

    public void Swap() => (Atk, Def) = (Def, Atk);

    public void Run() => Result = CombatSim.Simulate(Atk.ToSpec(), Def.ToSpec(), new SimOptions
    {
        DefenderBlocks = DefenderBlocks, AttackerSick = AttackerSick, DefenderTapped = DefenderTapped,
        DefenderControlsLand = DefenderControlsLand, AttackerLife = AttackerLife, DefenderLife = DefenderLife,
    });
}
