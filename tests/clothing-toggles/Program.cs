// Run: dotnet run --project tests/clothing-toggles
// Drives the production ClothingToggles: take off and put back exactly what you wore, per slot,
// forgotten for a new ped; and checks the menu and key bindings that use it.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using vMenuClient;
using static vMenuClient.ClothingToggles;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static int Main()
    {
        var toggles = new ClothingToggles();
        var cap = new Look(12, 3);
        Check(toggles.Toggle(1, Slot.Hat, cap, out _) == Outcome.TakenOff, "a worn hat is taken off");
        Check(toggles.Toggle(1, Slot.Hat, null, out var back) == Outcome.PutBack && back.Drawable == 12 && back.Texture == 3, "the same hat goes back on");
        Check(toggles.Toggle(1, Slot.Hat, null, out _) == Outcome.NothingToToggle, "nothing to put back twice");

        toggles.Toggle(1, Slot.Glasses, new Look(5, 0), out _);
        toggles.Toggle(1, Slot.Mask, new Look(40, 2), out _);
        Check(toggles.Toggle(1, Slot.Glasses, null, out back) == Outcome.PutBack && back.Drawable == 5, "slots are remembered separately");
        Check(toggles.Toggle(1, Slot.Mask, null, out back) == Outcome.PutBack && back.Drawable == 40 && back.Texture == 2, "the mask comes back with its texture");

        toggles.Toggle(1, Slot.Bag, new Look(45, 0), out _);
        toggles.Toggle(1, Slot.Bag, new Look(82, 1), out _);
        Check(toggles.Toggle(1, Slot.Bag, null, out back) == Outcome.PutBack && back.Drawable == 82, "taking off a newer item remembers that one");

        toggles.Toggle(1, Slot.Hat, cap, out _);
        Check(toggles.Toggle(2, Slot.Hat, null, out _) == Outcome.NothingToToggle, "a new ped forgets what the old one took off");

        Check(Slots.Select(s => s.Slot).Distinct().Count() == Enum.GetValues(typeof(Slot)).Length, "every slot is described once");
        Check(Slots.Where(s => s.IsProp).Select(s => s.Index).SequenceEqual(new[] { 0, 1, 2 }), "hat, glasses and ears are props 0, 1, 2");
        Check(Slots.Single(s => s.Slot == Slot.Mask) is { IsProp: false, Index: 1, MpOnly: true }, "the mask is component 1, MP characters only");
        Check(Slots.Single(s => s.Slot == Slot.Bag) is { IsProp: false, Index: 5, MpOnly: true }, "the bag is component 5, MP characters only");
        Console.WriteLine("PASS: take off and put back per slot, newest item, new ped, slot table");

        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        var appearance = File.ReadAllText(Path.Combine(dir, "vMenu", "menus", "PlayerAppearance.cs")).Replace("\r\n", "\n");
        Check(appearance.Contains("RegisterKeyMapping(command, $\"vMenu Toggle {info.Name}\", \"keyboard\", \"\");"), "key bindings are unbound by default");
        Check(appearance.Contains("var command = $\"vMenu:{keyMappingId}:Toggle{slot}\";"), "commands use the server's key mapping id");
        var toggle = Regex.Match(appearance, @"internal static void ToggleClothing\(ClothingToggles\.Slot slot\)\n        \{.*?\n        \}\n", RegexOptions.Singleline).Value;
        Check(toggle.IndexOf("IsPedDeadOrDying", StringComparison.Ordinal) < toggle.IndexOf("clothingToggles.Toggle(", StringComparison.Ordinal), "nothing changes while dead or ragdolled");
        Check(toggle.Contains("if (info.MpOnly && !IsMpCharacter(ped))"), "components only change on MP characters");
        Check(toggle.Contains("SetPedComponentVariation(ped, info.Index, 0, 0, 0);") && toggle.Contains("ClearPedProp(ped, info.Index);"), "taking off empties the slot");
        Console.WriteLine("PASS: Player Appearance menu and key binding wiring");
        return 0;
    }
}
