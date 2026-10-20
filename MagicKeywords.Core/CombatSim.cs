namespace MagicKeywords;

public enum LogKind { Phase, Info, Rule, Trigger, Stack, Resolve, Damage, Result }

public record LogLine(LogKind Kind, string Text, int Indent = 0);

public class CreatureSpec
{
    public string Name = "";
    public int Power, Toughness;
    public HashSet<char> Colors = new();   // W U B R G
    public bool Artifact;
    public Dictionary<string, int> Keywords = new(StringComparer.OrdinalIgnoreCase); // value = N (1 for flags)
}

public class SimOptions
{
    public bool DefenderBlocks = true;
    public bool AttackerSick;          // came under its controller's control this turn
    public bool DefenderTapped;
    public bool DefenderControlsLand;  // matches the attacker's landwalk type
    public int AttackerLife = 20, DefenderLife = 20;
}

public record SimKeyword(string Name, bool Numeric, string Group);

public static class SimKeywords
{
    public static readonly SimKeyword[] All =
    {
        new("Flying", false, "Evasion & blocking"), new("Reach", false, "Evasion & blocking"),
        new("Shadow", false, "Evasion & blocking"), new("Horsemanship", false, "Evasion & blocking"),
        new("Fear", false, "Evasion & blocking"), new("Intimidate", false, "Evasion & blocking"),
        new("Skulk", false, "Evasion & blocking"), new("Menace", false, "Evasion & blocking"),
        new("Landwalk", false, "Evasion & blocking"), new("Defender", false, "Evasion & blocking"),
        new("Decayed", false, "Evasion & blocking"), new("Haste", false, "Evasion & blocking"),
        new("Vigilance", false, "Evasion & blocking"),
        new("First Strike", false, "Damage"), new("Double Strike", false, "Damage"),
        new("Deathtouch", false, "Damage"), new("Trample", false, "Damage"),
        new("Lifelink", false, "Damage"), new("Infect", false, "Damage"), new("Wither", false, "Damage"),
        new("Toxic", true, "Damage"), new("Indestructible", false, "Damage"), new("Absorb", true, "Damage"),
        new("Protection from White", false, "Protection"), new("Protection from Blue", false, "Protection"),
        new("Protection from Black", false, "Protection"), new("Protection from Red", false, "Protection"),
        new("Protection from Green", false, "Protection"),
        new("Hexproof", false, "Protection"), new("Shroud", false, "Protection"),
        new("Exalted", true, "Triggers"), new("Battle Cry", false, "Triggers"),
        new("Flanking", false, "Triggers"), new("Bushido", true, "Triggers"), new("Rampage", true, "Triggers"),
        new("Afflict", true, "Triggers"), new("Annihilator", true, "Triggers"), new("Melee", false, "Triggers"),
        new("Mentor", false, "Triggers"), new("Provoke", false, "Triggers"), new("Dethrone", false, "Triggers"),
        new("Frenzy", true, "Triggers"), new("Renown", true, "Triggers"), new("Poisonous", true, "Triggers"),
        new("Ingest", false, "Triggers"), new("Persist", false, "Triggers"), new("Undying", false, "Triggers"),
        new("Afterlife", true, "Triggers"),
    };
}

public class CombatSim
{
    sealed class Perm
    {
        public string Label = "";
        public int Player;
        public Dictionary<string, int> Kw = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<char> Colors = new();
        public bool Artifact;
        public int P0, T0, Plus, Minus, TempP, TempT, Damage;
        public bool DeathtouchHit, Tapped, Dead, InCombat, Renowned;
        public bool FirstStepEligible;
        public int Power => P0 + Plus - Minus + TempP;
        public int Toughness => T0 + Plus - Minus + TempT;
        public string Pt => $"{Power}/{Toughness}";
        public bool Has(string k) => Kw.ContainsKey(k);
        public int N(string k) => Kw.TryGetValue(k, out var v) ? v : 0;
        public bool ProtectedFrom(Perm src) =>
            src.Colors.Any(c => Has("Protection from " + ColorName(c)));
        public bool HadMinus, HadPlus; // last-known information when it died
    }

    sealed record StackObj(int Controller, string Text, Action Resolve);

    readonly List<LogLine> log = new();
    readonly Perm A, B;
    readonly SimOptions o;
    readonly int[] life = new int[2];
    readonly int[] poison = new int[2];
    readonly List<StackObj> stack = new();
    readonly List<StackObj> pending = new();
    readonly Dictionary<Perm, string> fate = new();
    readonly int[] startLife = new int[2];
    string blockNote = "";
    bool blocked, blockerBlocking, mustBlock, decayedPending, fsStepHappened;

    static string ColorName(char c) => c switch { 'W' => "White", 'U' => "Blue", 'B' => "Black", 'R' => "Red", _ => "Green" };
    static string PName(int p) => p == 0 ? "Player 1" : "Player 2";

    CombatSim(CreatureSpec a, CreatureSpec b, SimOptions opt)
    {
        o = opt;
        A = Make(a, 0, "Creature 1");
        B = Make(b, 1, "Creature 2");
        if (string.Equals(A.Label, B.Label, StringComparison.OrdinalIgnoreCase)) B.Label += " (blocker)";
        life[0] = opt.AttackerLife; life[1] = opt.DefenderLife;
        startLife[0] = life[0]; startLife[1] = life[1];
        B.Tapped = opt.DefenderTapped;
    }

    static Perm Make(CreatureSpec s, int player, string fallback) => new()
    {
        Label = string.IsNullOrWhiteSpace(s.Name) ? fallback : s.Name.Trim(),
        Player = player,
        Kw = new Dictionary<string, int>(s.Keywords, StringComparer.OrdinalIgnoreCase),
        Colors = new HashSet<char>(s.Colors),
        Artifact = s.Artifact,
        P0 = s.Power, T0 = s.Toughness,
    };

    public static List<LogLine> Simulate(CreatureSpec a, CreatureSpec b, SimOptions o)
    {
        var sim = new CombatSim(a, b, o);
        sim.Run();
        return sim.log;
    }

    void L(LogKind k, string t, int ind = 0) => log.Add(new LogLine(k, t, ind));

    string Describe(Perm p)
    {
        var kws = p.Kw.Select(kv => SimKeywords.All.Any(x => x.Numeric && x.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
            ? $"{kv.Key} {kv.Value}" : kv.Key).ToList();
        var cols = p.Colors.Count == 0 ? "colorless" : string.Join("/", p.Colors.Select(ColorName)).ToLower();
        return $"{p.Label} ({p.Pt}, {cols}{(p.Artifact ? " artifact" : "")} creature{(kws.Count > 0 ? ": " + string.Join(", ", kws) : "")})";
    }

    // ------------------------------------------------------------------ main flow
    void Run()
    {
        L(LogKind.Phase, "Setup");
        L(LogKind.Info, $"Player 1 (active player, {life[0]} life) controls {Describe(A)}.");
        L(LogKind.Info, $"Player 2 (defending player, {life[1]} life) controls {Describe(B)}.");
        L(LogKind.Info, $"{A.Label} is the only attacker. {B.Label} {(o.DefenderBlocks ? "will block if it legally can" : "won't be assigned to block")}.");

        L(LogKind.Phase, "Beginning of Combat");
        L(LogKind.Rule, "The combat phase begins. Nothing in this simulation triggers at the beginning of combat. (CR 507)");

        L(LogKind.Phase, "Declare Attackers Step");
        if (DeclareAttackers())
        {
            L(LogKind.Phase, "Declare Blockers Step");
            if (A.InCombat) DeclareBlockers();

            bool anyFirst = (A.InCombat && !A.Dead && (A.Has("First Strike") || A.Has("Double Strike")))
                         || (blockerBlocking && B.InCombat && !B.Dead && (B.Has("First Strike") || B.Has("Double Strike")));
            if (A.InCombat)
            {
                if (anyFirst)
                {
                    L(LogKind.Phase, "First-Strike Combat Damage Step");
                    L(LogKind.Rule, "At least one attacking or blocking creature has first strike or double strike as the damage step begins, so the phase gets two combat damage steps. Only creatures with first strike or double strike deal damage in the first one. (CR 510.4)");
                    fsStepHappened = true;
                    A.FirstStepEligible = A.Has("First Strike") || A.Has("Double Strike");
                    B.FirstStepEligible = B.Has("First Strike") || B.Has("Double Strike");
                    DamageStep(true);
                    L(LogKind.Phase, "Regular Combat Damage Step");
                    L(LogKind.Rule, "Creatures that didn't deal damage in the first step (no first strike or double strike) deal damage now, as do creatures with double strike. (CR 510.4)");
                }
                else L(LogKind.Phase, "Combat Damage Step");
                DamageStep(false);
            }
            else
            {
                L(LogKind.Phase, "Combat Damage Step");
                L(LogKind.Info, "No creature is in combat, so no combat damage is dealt.");
            }
        }

        L(LogKind.Phase, "End of Combat Step");
        if (decayedPending && !A.Dead)
        {
            L(LogKind.Trigger, $"Delayed trigger: {A.Label}'s decayed ability set up a sacrifice \"at end of combat\". (CR 702.147a, 603.7)");
            RunStack(new() { new(A.Player, $"Decayed (delayed) — sacrifice {A.Label}", () => Sacrifice(A)) });
        }
        else L(LogKind.Info, "Nothing triggers at end of combat. Damage stays marked until the cleanup step, and \"until end of turn\" effects last until then too.");

        Summary();
    }

    // ------------------------------------------------------------------ declare attackers
    bool DeclareAttackers()
    {
        if (A.Has("Defender"))
        {
            L(LogKind.Rule, $"{A.Label} has defender, so it can't attack. (CR 702.3b)");
            L(LogKind.Result, "No attack is possible, so combat ends with nothing happening.");
            fate[A] = "didn't attack (defender)";
            return false;
        }
        if (o.AttackerSick && !A.Has("Haste"))
        {
            L(LogKind.Rule, $"{A.Label} came under Player 1's control this turn and has no haste, so it's summoning sick and can't attack. (CR 302.6)");
            L(LogKind.Result, "No attack is possible, so combat ends with nothing happening.");
            fate[A] = "didn't attack (summoning sick)";
            return false;
        }
        if (o.AttackerSick) L(LogKind.Rule, $"{A.Label} is summoning sick but haste lets it attack anyway. (CR 702.10b)");

        if (A.Has("Vigilance")) L(LogKind.Rule, $"{A.Label} has vigilance, so attacking doesn't cause it to tap. (CR 702.20b)");
        else { A.Tapped = true; L(LogKind.Rule, $"Attacking causes {A.Label} to tap. (CR 508.1f)"); }
        A.InCombat = true;
        L(LogKind.Info, $"{A.Label} attacks Player 2. It attacks alone. (CR 506.5)");

        var t = new List<StackObj>();
        for (int i = 1; i <= A.N("Exalted"); i++)
        {
            int n = i;
            t.Add(new(0, $"Exalted{(A.N("Exalted") > 1 ? $" (instance {n})" : "")} — {A.Label} gets +1/+1 until end of turn",
                () => Pump(A, 1, 1)));
        }
        if (A.Has("Battle Cry"))
            t.Add(new(0, "Battle cry — each other attacking creature gets +1/+0 until end of turn",
                () => L(LogKind.Info, $"There are no other attacking creatures, so battle cry affects nothing. ({A.Label} isn't affected: it says \"each other\".) (CR 702.91a)", 1)));
        if (A.Has("Melee"))
            t.Add(new(0, $"Melee — {A.Label} gets +1/+1 until end of turn for each opponent attacked", () => Pump(A, 1, 1)));
        if (A.Has("Dethrone"))
        {
            if (life[1] >= life[0])
                t.Add(new(0, $"Dethrone — put a +1/+1 counter on {A.Label}", () => { A.Plus++; L(LogKind.Info, $"{A.Label} gets a +1/+1 counter and is now {A.Pt}.", 1); }));
            else L(LogKind.Rule, $"Dethrone doesn't trigger: Player 2 ({life[1]} life) doesn't have the most life or tie for it (Player 1 has {life[0]}). (CR 702.105a)");
        }
        if (A.Has("Annihilator"))
        {
            int n = A.N("Annihilator");
            t.Add(new(0, $"Annihilator {n} — Player 2 sacrifices {n} permanent{(n == 1 ? "" : "s")}",
                () => L(LogKind.Info, $"Player 2 sacrifices {n} permanent{(n == 1 ? "" : "s")}. This simulation assumes they sacrifice other permanents (not {B.Label}), because the ability resolves before blockers are declared. (CR 702.86a)", 1)));
        }
        if (A.Has("Mentor"))
            L(LogKind.Rule, $"Mentor triggers, but it needs a target: an attacking creature with power less than {A.Label}'s. {A.Label} is the only attacker, so the ability has no legal target and is removed from the stack. (CR 702.134a, 603.3d)");
        if (A.Has("Provoke"))
        {
            if (B.Has("Hexproof") || B.Has("Shroud"))
                L(LogKind.Rule, $"Provoke triggers, but {B.Label} has {(B.Has("Shroud") ? "shroud" : "hexproof")}, so it can't be targeted by Player 1's ability. With no legal target, the trigger is removed from the stack. (CR 603.3d, 702.11b, 702.18a)");
            else
                t.Add(new(0, $"Provoke — target {B.Label}: untap it; it blocks {A.Label} this combat if able", () =>
                {
                    if (B.Tapped) { B.Tapped = false; L(LogKind.Info, $"{B.Label} untaps.", 1); }
                    else L(LogKind.Info, $"{B.Label} was already untapped.", 1);
                    mustBlock = true;
                    L(LogKind.Info, $"{B.Label} must block {A.Label} this combat if able.", 1);
                }));
        }
        if (A.Has("Decayed"))
            t.Add(new(0, $"Decayed — sacrifice {A.Label} at end of combat", () =>
            {
                decayedPending = true;
                L(LogKind.Info, "This creates a delayed triggered ability that will sacrifice it at end of combat.", 1);
            }));

        if (t.Count == 0) L(LogKind.Info, "No abilities trigger on attack.");
        else L(LogKind.Trigger, $"{t.Count} ability trigger{(t.Count == 1 ? "s" : "")} when {A.Label} attacks: {string.Join("; ", t.Select(x => x.Text.Split(" — ")[0]))}.");
        RunStack(t);
        return A.InCombat;
    }

    // ------------------------------------------------------------------ declare blockers
    void DeclareBlockers()
    {
        if (B.Dead)
        {
            L(LogKind.Info, $"{B.Label} is no longer on the battlefield and can't block.");
            Unblocked();
            return;
        }
        bool wants = o.DefenderBlocks || mustBlock;
        if (!wants)
        {
            L(LogKind.Info, "Player 2 declares no blockers.");
            blockNote = $"{A.Label} was unblocked because Player 2 chose not to block.";
            Unblocked();
            return;
        }
        var reasons = BlockProblems();
        if (reasons.Count > 0)
        {
            L(LogKind.Rule, $"{B.Label} can't legally block {A.Label}:");
            foreach (var r in reasons) L(LogKind.Rule, "• " + r, 1);
            if (mustBlock) L(LogKind.Info, "Provoke only requires a block \"if able\", so there's no requirement here.");
            L(LogKind.Info, $"{A.Label} is unblocked.");
            blockNote = $"{A.Label} was unblocked because {B.Label} couldn't legally block it: {string.Join(" ", reasons.Select(x => x.Substring(0, x.LastIndexOf(" (CR") > 0 ? x.LastIndexOf(" (CR") : x.Length)))}";
            Unblocked();
            return;
        }

        L(LogKind.Info, $"Player 2 declares {B.Label} as a blocker, blocking {A.Label}.{(mustBlock && !o.DefenderBlocks ? " (Provoke forces it.)" : "")}");
        blocked = true; blockerBlocking = true; B.InCombat = true;
        blockNote = $"{B.Label} blocked {A.Label}, so {A.Label} can't deal combat damage to Player 2 directly unless it has trample; it assigns its damage to the blocker instead.";

        var t = new List<StackObj>();
        if (A.Has("Flanking") && !B.Has("Flanking"))
            t.Add(new(0, $"Flanking — {B.Label} gets -1/-1 until end of turn", () => Pump(B, -1, -1)));
        else if (A.Has("Flanking"))
            L(LogKind.Rule, $"Flanking triggers only if the blocker lacks flanking; {B.Label} has it, so nothing happens. (CR 702.25a)");
        if (A.Has("Bushido"))
        {
            int n = A.N("Bushido");
            t.Add(new(0, $"Bushido {n} (on {A.Label}, becomes blocked) — +{n}/+{n} until end of turn", () => Pump(A, n, n)));
        }
        if (A.Has("Rampage"))
        {
            int n = A.N("Rampage");
            t.Add(new(0, $"Rampage {n} — {A.Label} gets +{n}/+{n} for each creature blocking it beyond the first", () =>
                L(LogKind.Info, $"Only one creature is blocking, so there are 0 creatures beyond the first: {A.Label} gets +0/+0. (CR 702.23a)", 1)));
        }
        if (A.Has("Afflict"))
        {
            int n = A.N("Afflict");
            t.Add(new(0, $"Afflict {n} — Player 2 loses {n} life", () => { life[1] -= n; L(LogKind.Info, $"Player 2 loses {n} life ({life[1]} life).", 1); }));
        }
        if (B.Has("Bushido"))
        {
            int n = B.N("Bushido");
            t.Add(new(1, $"Bushido {n} (on {B.Label}, blocks) — +{n}/+{n} until end of turn", () => Pump(B, n, n)));
        }
        if (t.Count == 0) L(LogKind.Info, "No abilities trigger on blocks.");
        else L(LogKind.Trigger, $"{t.Count} ability trigger{(t.Count == 1 ? "s" : "")} from the block: {string.Join("; ", t.Select(x => x.Text.Split(" — ")[0]))}.");
        RunStack(t);
    }

    void Unblocked()
    {
        blocked = false; blockerBlocking = false;
        var t = new List<StackObj>();
        if (A.Has("Frenzy"))
        {
            int n = A.N("Frenzy");
            t.Add(new(0, $"Frenzy {n} — {A.Label} gets +{n}/+0 until end of turn (it attacked and wasn't blocked)", () => Pump(A, n, 0)));
        }
        if (t.Count > 0) { L(LogKind.Trigger, "Frenzy triggers because the attacker isn't blocked."); RunStack(t); }
    }

    List<string> BlockProblems()
    {
        var r = new List<string>();
        if (B.Tapped) r.Add("It's tapped, and only untapped creatures can block. (CR 509.1a)");
        if (B.Has("Decayed")) r.Add("It has decayed, so it can't block. (CR 702.147a)");
        if (A.Has("Flying") && !B.Has("Flying") && !B.Has("Reach")) r.Add($"{A.Label} has flying and {B.Label} has neither flying nor reach. (CR 702.9b)");
        if (A.Has("Shadow") && !B.Has("Shadow")) r.Add($"{A.Label} has shadow, which can be blocked only by creatures with shadow. (CR 702.28b)");
        if (!A.Has("Shadow") && B.Has("Shadow")) r.Add($"{B.Label} has shadow, so it can block only creatures with shadow. (CR 702.28b)");
        if (A.Has("Horsemanship") && !B.Has("Horsemanship")) r.Add($"{A.Label} has horsemanship, which can be blocked only by creatures with horsemanship. (CR 702.31b)");
        if (A.Has("Fear") && !(B.Artifact || B.Colors.Contains('B'))) r.Add($"{A.Label} has fear, so only artifact and/or black creatures can block it. (CR 702.36b)");
        if (A.Has("Intimidate") && !(B.Artifact || A.Colors.Overlaps(B.Colors))) r.Add($"{A.Label} has intimidate, so only artifact creatures and creatures sharing a color with it can block. (CR 702.13b)");
        if (A.Has("Skulk") && B.Power > A.Power) r.Add($"{A.Label} has skulk and {B.Label}'s power ({B.Power}) is greater than its power ({A.Power}). (CR 702.118b)");
        if (A.Has("Menace")) r.Add($"{A.Label} has menace, which can be blocked only by two or more creatures, and Player 2 has only one creature. (CR 702.111b)");
        if (A.Has("Landwalk") && o.DefenderControlsLand) r.Add($"{A.Label} has landwalk and Player 2 controls a matching land, so it can't be blocked. (CR 702.14c)");
        foreach (var c in B.Colors)
            if (A.Has("Protection from " + ColorName(c))) r.Add($"{A.Label} has protection from {ColorName(c).ToLower()}, so it can't be blocked by {ColorName(c).ToLower()} creatures. (CR 702.16f)");
        return r;
    }

    // ------------------------------------------------------------------ damage
    void DamageStep(bool firstStrike)
    {
        bool AEligible = Eligible(A, firstStrike), BEligible = Eligible(B, firstStrike);
        bool aDeals = A.InCombat && !A.Dead && AEligible;
        bool bDeals = blockerBlocking && B.InCombat && !B.Dead && BEligible && A.InCombat && !A.Dead;
        // A creature that's been removed from combat (e.g. died) may still count as "blocked" for trample purposes.

        if (!aDeals && !bDeals)
        {
            if (blockerBlocking && !B.Dead && !A.InCombat)
                L(LogKind.Info, $"{A.Label} is no longer in combat, so {B.Label} has nothing to deal combat damage to. (CR 510.1d)");
            else if (blocked && B.Dead && blockerBlocking)
                L(LogKind.Info, $"{B.Label} is no longer on the battlefield, so it deals no combat damage, and {A.Label} has nothing left to assign damage to.");
            else L(LogKind.Info, "No creature assigns combat damage in this step.");
            return;
        }

        L(LogKind.Rule, "Each creature assigns damage equal to its power, then all combat damage is dealt simultaneously with no chance to respond. (CR 510.1a, 510.2)");
        var hits = new List<(Perm src, Perm? creature, int player, int amt)>();

        if (!aDeals && A.InCombat && !A.Dead)
            L(LogKind.Info, $"{A.Label} {(firstStrike ? "has no first strike or double strike, so it deals no damage yet" : "already dealt its damage in the first-strike step")}.");
        if (aDeals)
        {
            int p = A.Power;
            if (p <= 0) L(LogKind.Info, $"{A.Label} has {p} power, so it assigns no combat damage. (CR 510.1a)");
            else if (blocked)
            {
                if (blockerBlocking && B.InCombat && !B.Dead)
                {
                    if (A.Has("Trample"))
                    {
                        int lethal = Lethal(A, B);
                        int toB = Math.Min(p, lethal);
                        int rest = p - toB;
                        L(LogKind.Info, $"{A.Label} has trample: it must assign lethal damage to {B.Label} ({lethal}{(A.Has("Deathtouch") ? ", because deathtouch makes 1 damage lethal" : "")}), and may assign the rest to Player 2. It assigns {toB} to {B.Label} and {rest} to Player 2. (CR 702.19b)");
                        if (toB > 0) hits.Add((A, B, -1, toB));
                        if (rest > 0) hits.Add((A, null, 1, rest));
                    }
                    else
                    {
                        L(LogKind.Info, $"{A.Label} is blocked by one creature, so it assigns all {p} damage to {B.Label}. (CR 510.1c)");
                        hits.Add((A, B, -1, p));
                    }
                }
                else if (A.Has("Trample"))
                {
                    L(LogKind.Info, $"{A.Label} was blocked, but nothing is blocking it now. With trample it assigns all {p} damage to Player 2 as though lethal damage was assigned to the blockers. (CR 702.19d)");
                    hits.Add((A, null, 1, p));
                }
                else L(LogKind.Info, $"{A.Label} remains blocked, but no creature is blocking it now, so it assigns no combat damage. It does NOT deal damage to Player 2. (CR 510.1c)");
            }
            else
            {
                L(LogKind.Info, $"{A.Label} is unblocked, so it assigns {p} damage to Player 2.");
                hits.Add((A, null, 1, p));
            }
        }
        if (bDeals)
        {
            int p = B.Power;
            if (p <= 0) L(LogKind.Info, $"{B.Label} has {p} power, so it assigns no combat damage. (CR 510.1a)");
            else { L(LogKind.Info, $"{B.Label} assigns {p} damage to {A.Label}, the creature it's blocking."); hits.Add((B, A, -1, p)); }
        }
        else if (blockerBlocking && B.InCombat && !B.Dead && !BEligible)
            L(LogKind.Info, $"{B.Label} {(firstStrike ? "has no first strike or double strike, so it deals no damage yet" : "already dealt its damage in the first-strike step")}.");
        else if (blockerBlocking && B.InCombat && !B.Dead && !A.InCombat)
            L(LogKind.Info, $"{A.Label} is no longer in combat, so {B.Label} has nothing to deal damage to.");

        // deal simultaneously
        var playerHit = new HashSet<Perm>();
        var lifegain = new List<(Perm src, int amt)>();
        foreach (var h in hits)
        {
            int dealt = Deal(h.src, h.creature, h.player, h.amt, true);
            if (dealt > 0 && h.creature == null) playerHit.Add(h.src);
        }

        // SBAs are checked before anything goes on the stack
        L(LogKind.Rule, "State-based actions are checked before any player would receive priority, and triggered abilities wait until after. (CR 704.3)");
        Sba();

        var t = pending.ToList(); pending.Clear();
        foreach (var src in playerHit)
        {
            if (src.Has("Renown") && !src.Renowned && !src.Dead)
            {
                int n = src.N("Renown");
                t.Add(new(src.Player, $"Renown {n} — if {src.Label} isn't renowned, put {n} +1/+1 counter{(n == 1 ? "" : "s")} on it and it becomes renowned", () =>
                {
                    if (src.Renowned || src.Dead) { L(LogKind.Info, "The condition is no longer true or it left, so nothing happens. (CR 603.4)", 1); return; }
                    src.Plus += n; src.Renowned = true;
                    L(LogKind.Info, $"{src.Label} gets {n} +1/+1 counter{(n == 1 ? "" : "s")} and becomes renowned. It's now {src.Pt}.", 1);
                }));
            }
            if (src.Has("Poisonous"))
            {
                int n = src.N("Poisonous"); int pl = 1 - src.Player;
                t.Add(new(src.Player, $"Poisonous {n} — {PName(pl)} gets {n} poison counter{(n == 1 ? "" : "s")}", () =>
                { poison[pl] += n; L(LogKind.Info, $"{PName(pl)} now has {poison[pl]} poison counter{(poison[pl] == 1 ? "" : "s")}.", 1); }));
            }
            if (src.Has("Ingest"))
                t.Add(new(src.Player, $"Ingest — {PName(1 - src.Player)} exiles the top card of their library", () =>
                    L(LogKind.Info, $"{PName(1 - src.Player)} exiles the top card of their library.", 1)));
        }
        if (t.Count > 0) L(LogKind.Trigger, $"{t.Count} triggered abilit{(t.Count == 1 ? "y" : "ies")} from combat damage and/or creatures dying: {string.Join("; ", t.Select(x => x.Text.Split(" — ")[0]))}.");
        RunStack(t);
    }

    bool Eligible(Perm p, bool firstStep)
    {
        if (firstStep) return p.Has("First Strike") || p.Has("Double Strike");
        if (!fsStepHappened) return true;
        return p.Has("Double Strike") || !p.FirstStepEligible;
    }

    static int Lethal(Perm src, Perm target)
    {
        int remaining = target.Toughness - target.Damage;
        if (remaining <= 0) return 0;
        return src.Has("Deathtouch") ? 1 : remaining;
    }

    // Returns damage actually dealt after prevention. creature != null => to that creature; else to player index `player`.
    int Deal(Perm src, Perm? c, int player, int amt, bool combat)
    {
        string tn = c != null ? c.Label : PName(player);
        string kind = combat ? "combat damage" : "damage";
        if (c != null)
        {
            if (c.ProtectedFrom(src))
            {
                L(LogKind.Damage, $"{src.Label} would deal {amt} {kind} to {c.Label}, but all of it is prevented by protection from {string.Join("/", src.Colors.Where(x => c.Has("Protection from " + ColorName(x))).Select(x => ColorName(x).ToLower()))}. (CR 702.16e)", 1);
                return 0;
            }
            int absorb = c.N("Absorb");
            if (absorb > 0)
            {
                int prev = Math.Min(absorb, amt);
                amt -= prev;
                L(LogKind.Damage, $"{c.Label}'s absorb {absorb} prevents {prev} of the damage from {src.Label}. (CR 702.64a)", 1);
                if (amt == 0) return 0;
            }
        }

        if (c != null)
        {
            if (src.Has("Infect") || src.Has("Wither"))
            {
                c.Minus += amt;
                string kw = src.Has("Infect") ? "infect" : "wither";
                L(LogKind.Damage, $"{src.Label} deals {amt} {kind} to {c.Label}. Because it has {kw}, that's {amt} -1/-1 counter{(amt == 1 ? "" : "s")} instead of marked damage. {c.Label} is now {c.Pt}. (CR {(src.Has("Infect") ? "702.90c" : "702.80a")})", 1);
            }
            else
            {
                c.Damage += amt;
                L(LogKind.Damage, $"{src.Label} deals {amt} {kind} to {c.Label}, which now has {c.Damage} damage marked on a creature with toughness {c.Toughness}.", 1);
            }
            if (src.Has("Deathtouch"))
            {
                c.DeathtouchHit = true;
                L(LogKind.Damage, $"{src.Label} has deathtouch, so {c.Label} is destroyed as a state-based action no matter how much damage it took, unless something prevents it. (CR 702.2b)", 1);
            }
        }
        else if (src.Has("Infect"))
        {
            poison[player] += amt;
            L(LogKind.Damage, $"{src.Label} deals {amt} {kind} to {tn}. Because it has infect, {tn} gets {amt} poison counter{(amt == 1 ? "" : "s")} instead of losing life ({poison[player]} total). (CR 702.90b)", 1);
        }
        else
        {
            life[player] -= amt;
            L(LogKind.Damage, $"{src.Label} deals {amt} {kind} to {tn} ({life[player]} life left).", 1);
        }

        if (c == null && src.Has("Toxic"))
        {
            int n = src.N("Toxic");
            poison[player] += n;
            L(LogKind.Damage, $"{src.Label} has toxic {n}, so combat damage to {tn} also gives {n} poison counter{(n == 1 ? "" : "s")} ({poison[player]} total). (CR 702.164c)", 1);
        }
        if (src.Has("Lifelink"))
        {
            life[src.Player] += amt;
            L(LogKind.Damage, $"Lifelink: {PName(src.Player)} gains {amt} life as the damage is dealt ({life[src.Player]} life). (CR 702.15b)", 1);
        }
        return amt;
    }

    // ------------------------------------------------------------------ state-based actions and the stack
    void Sba()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var p in new[] { A, B })
            {
                if (p.Dead) continue;
                if (p.Plus > 0 && p.Minus > 0)
                {
                    int n = Math.Min(p.Plus, p.Minus);
                    p.Plus -= n; p.Minus -= n;
                    L(LogKind.Rule, $"{p.Label} has both +1/+1 and -1/-1 counters, so {n} of each are removed. (CR 704.5q)");
                    changed = true;
                }
            }
            var dying = new List<(Perm p, string why)>();
            foreach (var p in new[] { A, B })
            {
                if (p.Dead) continue;
                if (p.Toughness <= 0) dying.Add((p, $"toughness {p.Toughness} (CR 704.5f)"));
                else if (p.DeathtouchHit && !p.Has("Indestructible")) dying.Add((p, "damage from a source with deathtouch (CR 704.5h)"));
                else if (p.Damage >= p.Toughness && !p.Has("Indestructible")) dying.Add((p, $"lethal damage: {p.Damage} damage marked with toughness {p.Toughness} (CR 704.5g)"));
                else if ((p.DeathtouchHit || p.Damage >= p.Toughness) && p.Has("Indestructible"))
                {
                    if (indestructibleNoted.Add(p) || p.DeathtouchHit)
                        L(LogKind.Rule, $"{p.Label} would be destroyed by {(p.DeathtouchHit ? "deathtouch damage" : "lethal damage")}, but indestructible means it isn't destroyed. (CR 702.12b)");
                    p.DeathtouchHit = false;
                }
            }
            foreach (var (p, why) in dying)
            {
                L(LogKind.Rule, $"{p.Label} dies: {why}.");
                Kill(p, "died: " + why);
                changed = true;
            }
            for (int i = 0; i < 2; i++)
            {
                if (life[i] <= 0 && !lost.Contains(i)) { lost.Add(i); L(LogKind.Rule, $"{PName(i)} has {life[i]} life, so they lose the game. (CR 704.5a)"); }
                if (poison[i] >= 10 && !lost.Contains(i)) { lost.Add(i); L(LogKind.Rule, $"{PName(i)} has {poison[i]} poison counters, so they lose the game. (CR 704.5c)"); }
            }
        } while (changed);
    }
    readonly HashSet<Perm> indestructibleNoted = new();
    readonly HashSet<int> lost = new();

    void Kill(Perm p, string reason)
    {
        if (p.Dead) return;
        p.HadMinus = p.Minus > 0; p.HadPlus = p.Plus > 0;
        p.Dead = true; p.InCombat = false; p.DeathtouchHit = false;
        if (!fate.ContainsKey(p) || fate[p].StartsWith("survived")) fate[p] = reason;
        if (p.Has("Persist"))
            pending.Add(new(p.Player, $"Persist ({p.Label} died) — return it with a -1/-1 counter if it had none", () =>
            {
                if (p.HadMinus) { L(LogKind.Info, $"{p.Label} had a -1/-1 counter on it when it died, so persist does nothing. (CR 603.4)", 1); return; }
                Revive(p, minus: 1);
                L(LogKind.Info, $"{p.Label} returns to the battlefield with a -1/-1 counter, as a new object: it's {p.Pt}, untapped, and removed from combat. (CR 702.79a, 506.4)", 1);
            }));
        if (p.Has("Undying"))
            pending.Add(new(p.Player, $"Undying ({p.Label} died) — return it with a +1/+1 counter if it had none", () =>
            {
                if (p.HadPlus) { L(LogKind.Info, $"{p.Label} had a +1/+1 counter on it when it died, so undying does nothing. (CR 603.4)", 1); return; }
                Revive(p, plus: 1);
                L(LogKind.Info, $"{p.Label} returns to the battlefield with a +1/+1 counter, as a new object: it's {p.Pt}, untapped, and removed from combat. (CR 702.93a, 506.4)", 1);
            }));
        if (p.Has("Afterlife"))
        {
            int n = p.N("Afterlife");
            pending.Add(new(p.Player, $"Afterlife {n} ({p.Label} died) — create {n} 1/1 white and black Spirit token{(n == 1 ? "" : "s")} with flying", () =>
                L(LogKind.Info, $"{PName(p.Player)} creates {n} 1/1 white and black Spirit creature token{(n == 1 ? "" : "s")} with flying. (They're not in combat.) (CR 702.135a)", 1)));
        }
    }

    void Revive(Perm p, int plus = 0, int minus = 0)
    {
        p.Dead = false; p.InCombat = false; p.Tapped = false;
        p.Damage = 0; p.TempP = 0; p.TempT = 0; p.Plus = plus; p.Minus = minus; p.Renowned = false;
        p.DeathtouchHit = false;
        if (fate.ContainsKey(p)) fate[p] = "died, then returned: " + fate[p].Replace("died: ", "");
        if (p == B) blockerBlocking = false;
    }

    void Sacrifice(Perm p)
    {
        if (p.Dead) { L(LogKind.Info, $"{p.Label} is already gone.", 1); return; }
        L(LogKind.Info, $"{p.Label} is sacrificed. Sacrifice isn't destruction, so indestructible doesn't help. (CR 701.21a)", 1);
        Kill(p, "sacrificed (decayed)");
    }

    void Pump(Perm p, int dp, int dt)
    {
        p.TempP += dp; p.TempT += dt;
        L(LogKind.Info, $"{p.Label} gets {(dp >= 0 ? "+" : "")}{dp}/{(dt >= 0 ? "+" : "")}{dt} until end of turn and is now {p.Pt}.", 1);
    }

    void Push(List<StackObj> trig)
    {
        // APNAP: the active player puts theirs on first, so the non-active player's resolve first.
        foreach (var x in trig.Where(x => x.Controller == 0).Concat(trig.Where(x => x.Controller == 1))) stack.Add(x);
        L(LogKind.Rule, "Triggered abilities go on the stack in APNAP order: the active player's first, then the non-active player's, so the non-active player's abilities end up on top and resolve first. (CR 603.3b)");
        ShowStack();
    }

    void ShowStack()
    {
        L(LogKind.Stack, "The stack (top resolves first):");
        for (int i = stack.Count - 1, n = 1; i >= 0; i--, n++)
            L(LogKind.Stack, $"{n}. [{PName(stack[i].Controller)}] {stack[i].Text}", 1);
    }

    void RunStack(List<StackObj> trig)
    {
        if (trig.Count == 0) return;
        Push(trig);
        L(LogKind.Rule, "Both players pass priority in succession, so the top object resolves. (CR 405.5)");
        while (stack.Count > 0)
        {
            var top = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            L(LogKind.Resolve, $"Resolves: [{PName(top.Controller)}] {top.Text}");
            top.Resolve();
            Sba();
            if (pending.Count > 0)
            {
                var np = pending.ToList(); pending.Clear();
                L(LogKind.Trigger, $"New trigger{(np.Count == 1 ? "" : "s")}: {string.Join("; ", np.Select(x => x.Text.Split(" — ")[0]))}.");
                Push(np);
            }
            else if (stack.Count > 0) ShowStack();
        }
        L(LogKind.Info, "The stack is empty.");
    }

    // ------------------------------------------------------------------ summary
    void Summary()
    {
        L(LogKind.Phase, "Result");
        foreach (var p in new[] { A, B })
        {
            string status = p.Dead
                ? (fate.TryGetValue(p, out var f) ? f : "died")
                : fate.TryGetValue(p, out var f2) && f2.StartsWith("didn't attack") ? f2 : "survived";
            if (!p.Dead && fate.TryGetValue(p, out var f3) && f3.StartsWith("died, then")) status = f3;
            string state = p.Dead ? "" : $" — now {p.Pt}{(p.Damage > 0 ? $", {p.Damage} damage marked" : "")}{(p.Plus > 0 ? $", {p.Plus} +1/+1 counter(s)" : "")}{(p.Minus > 0 ? $", {p.Minus} -1/-1 counter(s)" : "")}";
            L(LogKind.Result, $"{p.Label}: {status}{state}");
        }
        for (int i = 0; i < 2; i++)
        {
            string ch = life[i] == startLife[i] ? "unchanged" : $"{startLife[i]} → {life[i]}";
            string extra = poison[i] > 0 ? $", {poison[i]} poison counter{(poison[i] == 1 ? "" : "s")}" : "";
            string lose = lost.Contains(i) ? " — LOSES THE GAME" : "";
            L(LogKind.Result, $"{PName(i)}: life {ch}{extra}{lose}");
        }

        // plain-language takeaways
        L(LogKind.Phase, "Why it turned out this way");
        if (blockNote.Length > 0) L(LogKind.Info, blockNote);
        if (!A.InCombat && !blocked && !fate.ContainsKey(A) && life[1] == startLife[1] && poison[1] == 0 && !A.Dead && !B.Dead)
            L(LogKind.Info, "Combat damage never happened.");
        for (int i = 0; i < 2; i++)
            if (life[i] < startLife[i]) L(LogKind.Info, $"{PName(i)} went from {startLife[i]} to {life[i]} life.");
        if (A.Dead && B.Dead) L(LogKind.Info, $"Both creatures died. Combat damage is dealt simultaneously, so {A.Label} and {B.Label} each dealt damage to the other.");
        else if (A.Dead) L(LogKind.Info, $"{A.Label} died: {Reason(A)}.");
        else if (!A.Dead && !B.Dead && A.Damage == 0 && B.Damage == 0 && A.Minus == 0 && B.Minus == 0 && blocked) L(LogKind.Info, "Neither creature died.");
        else if (B.Dead) L(LogKind.Info, $"{B.Label} died: {Reason(B)}.");
        foreach (var p in new[] { A, B })
            if (!p.Dead && fate.TryGetValue(p, out var rv) && rv.StartsWith("died, then"))
                L(LogKind.Info, $"{p.Label} died but came back, so it is a new object with {p.Pt} stats and isn't in combat anymore.");
        foreach (var p in new[] { A, B })
            if (!p.Dead && p.Damage > 0 && p.Toughness > p.Damage)
                L(LogKind.Info, $"{p.Label} survived because it was dealt {p.Damage} damage, less than its toughness of {p.Toughness}.");
        foreach (var p in indestructibleNoted)
            L(LogKind.Info, $"{p.Label} survived lethal damage / deathtouch because it's indestructible.");
        if (blocked && !blockerBlocking && !B.Dead && A.InCombat == false && life[1] == startLife[1] && !A.Has("Trample") && !A.Dead)
            L(LogKind.Info, "Once blocked, an attacker stays blocked even if its blocker leaves, so it dealt no damage to the player unless it had trample.");
    }

    string Reason(Perm p) => fate.TryGetValue(p, out var f) ? f.Replace("died: ", "") : "it was destroyed";
}
