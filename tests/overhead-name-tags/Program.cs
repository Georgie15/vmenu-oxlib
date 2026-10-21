// Run: dotnet run --project tests/overhead-name-tags
// Drives the production OverheadNameTags with fake gamer tags: one tag per nearby player, no
// re-creation while it is active, and tags of players who leave or move away are removed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using vMenuClient;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static int Main()
    {
        var active = new HashSet<int>();
        var nextTag = 1;
        var created = 0;
        var removed = new List<int>();
        int Create() { created++; var tag = nextTag++; active.Add(tag); return tag; }
        void Remove(int tag) { removed.Add(tag); active.Remove(tag); }

        var tags = new OverheadNameTags();
        int Update(params (int id, bool close)[] players)
        {
            var last = -1;
            tags.BeginUpdate();
            foreach (var (id, close) in players) last = tags.Visit(id, close, active.Contains, Create, Remove);
            tags.EndUpdate(Remove);
            return last;
        }

        var first = Update((7, true));
        Check(created == 1 && first == 1, "a nearby player gets a tag");
        for (var i = 0; i < 20; i++) Update((7, true));
        Check(created == 1, "an active tag is never re-created (it was, every 500 ms)");

        active.Remove(first); // the game dropped it (e.g. the ped respawned)
        var second = Update((7, true));
        Check(created == 2 && second == 2, "an inactive tag is replaced");

        Check(Update((7, false)) == -1 && removed.Contains(2) && tags.Count == 0, "moving away removes the tag");

        Update((7, true), (8, true));
        Check(tags.Count == 2, "two nearby players, two tags");
        removed.Clear();
        Update((7, true));
        Check(tags.Count == 1 && removed.Count == 1, "a player who left loses their tag (it used to stay forever)");

        removed.Clear();
        tags.Clear(Remove);
        Check(tags.Count == 0 && removed.Count == 1, "turning names off removes every tag");
        Console.WriteLine("PASS: one tag per nearby player, no re-creation, removed when away, gone or off");

        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        var fc = File.ReadAllText(Path.Combine(dir, "vMenu", "FunctionsController.cs"));
        Check(Regex.Matches(fc, @"CreateMpGamerTag\(").Count == 1 && fc.Contains("gamerTags.Visit(") && fc.Contains("gamerTags.EndUpdate("),
            "overhead names go through OverheadNameTags");
        Console.WriteLine("PASS: FunctionsController wiring");
        return 0;
    }
}
