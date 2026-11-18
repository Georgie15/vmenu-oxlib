// Run: dotnet run --project tests/player-guide
// Runs the production Player Guide content reader, step flow, key routing and button hold
// (vMenu/PlayerGuideContent.cs) against the packaged config/player-guide.json and broken copies of it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using vMenuClient;

static class Program
{
    static int checks;

    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "AGENTS.md"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new Exception("repository root not found");
    }

    static GuideContent ParseOk(string json, string what)
    {
        var c = GuideContent.Parse(json, out var error);
        Check(c != null, $"{what} parses ({error})");
        return c;
    }

    static void ParseFails(string json, string expected, string what)
    {
        var c = GuideContent.Parse(json, out var error);
        Check(c == null, $"{what} is refused");
        Check(error != null && error.Contains(expected), $"{what}: the error says \"{expected}\" (got \"{error}\")");
    }

    static string Mutate(string json, Action<JObject> change)
    {
        var o = JObject.Parse(json);
        change(o);
        return o.ToString();
    }

    static int Main()
    {
        var packaged = File.ReadAllText(Path.Combine(Root(), "build", "vMenu", "config", "player-guide.json"));

        // ---- The packaged file ----
        var content = ParseOk(packaged, "the packaged guide");
        Check(content.Version >= 1, "a version");
        Check(content.Welcome.Highlight.SequenceEqual(new[] { "Player Guide" }), "the welcome points at the Player Guide row");
        Check(content.Steps.Count >= 6 && content.Steps.Count <= 12, "a short guide: 6 to 12 steps");
        Check(content.Steps.Select(s => s.Id).Distinct().Count() == content.Steps.Count, "step ids are unique");
        Check(content.Steps.First().Id == "menu" && content.Steps.Last().Id == "done", "starts with the keys, ends with where to find it again");
        Check(content.Steps.Last().Highlight.Contains("Player Guide"), "the last step points at the Player Guide row");
        foreach (var card in content.Steps.Concat(content.Questions).Append(content.Welcome))
        {
            Check(GuideContent.MenuIds.Contains(card.Menu), $"{card.Title}: a known menu");
            Check(card.Body.All(p => p.Length <= 260), $"{card.Title}: short paragraphs");
            Check(card.Body.Count <= 3, $"{card.Title}: three paragraphs at most");
            foreach (var text in card.Body.Append(card.Tip ?? "").Append(card.Foot ?? ""))
            {
                Check(text.Split("**").Length % 2 == 1, $"{card.Title}: every ** is closed");
                Check(text.Count(ch => ch == '[') == text.Count(ch => ch == ']'), $"{card.Title}: every [KEY] is closed");
                Check(!text.Contains('<') && !text.Contains('>'), $"{card.Title}: no HTML (the page shows text only)");
            }
        }
        var uniformQuestion = content.Questions.Single(q => q.Title == "How do I get my department uniform?");
        Check(uniformQuestion.RequiresConvar == "vmenu_dept_outfits", "a question on a step keeps that step's convar");
        Check(uniformQuestion.Body.SequenceEqual(content.Steps.Single(s => s.Id == "uniforms").Body), "a question on a step uses its text");

        // ---- Broken files are refused, with a reason ----
        ParseFails("", "missing or empty", "an empty file");
        ParseFails("{ \"steps\": [", "valid JSON", "cut-off JSON");
        ParseFails(Mutate(packaged, o => o["welcome"].Parent.Remove()), "\"welcome\" is missing", "no welcome");
        ParseFails(Mutate(packaged, o => o["steps"] = new JArray()), "at least one step", "no steps");
        ParseFails(Mutate(packaged, o => o["version"] = 0), "\"version\"", "version 0");
        ParseFails(Mutate(packaged, o => o["version"] = "2"), "\"version\"", "a version in quotes");
        ParseFails(Mutate(packaged, o => o["steps"][1]["hightlight"] = new JArray("Saved Outfits")), "unknown setting \"hightlight\"", "a misspelt setting");
        ParseFails(Mutate(packaged, o => o["steps"][1]["menu"] = "mpPeds"), "unknown menu \"mpPeds\"", "an unknown menu");
        ParseFails(Mutate(packaged, o => o["steps"][1]["body"] = new JArray()), "\"body\"", "an empty body");
        ParseFails(Mutate(packaged, o => o["steps"][1]["title"].Parent.Remove()), "\"title\" is missing", "no title");
        ParseFails(Mutate(packaged, o => o["steps"][2]["id"] = o["steps"][1]["id"]), "two steps have the id", "duplicate ids");
        ParseFails(Mutate(packaged, o => o["questions"][0]["step"] = "nope"), "no step \"nope\"", "a question on a missing step");
        ParseFails(Mutate(packaged, o => o["questions"][0]["body"] = new JArray("x")), "either \"step\" or its own", "a question with a step and its own text");
        ParseFails(Mutate(packaged, o => o["steps"][1]["highlight"] = "Saved Outfits"), "\"highlight\" must be a list", "highlight as text");
        ParseFails(Mutate(packaged, o => o["steps"][1]["tip"] = 3), "\"tip\" must be text", "a number as a tip");

        // ---- The flow, with every card available ----
        var all = new PlayerGuideFlow(content, _ => true);
        var n = content.Steps.Count;
        var v = all.Welcome();
        Check(v.Mode == GuideMode.Welcome && v.Card == content.Welcome, "first open: the welcome");
        v = all.Back(out var end);
        Check(v != null && v.Mode == GuideMode.Welcome && end == GuideEnd.None, "Back on the welcome stays there");
        v = all.Next(out end);
        Check(v.Mode == GuideMode.Tour && v.Number == 1 && v.Count == n && v.Card.Id == "menu", "Enter starts at step 1 of n");
        v = all.Back(out end);
        Check(v.Mode == GuideMode.Welcome && end == GuideEnd.None, "Back from step 1 returns to the welcome");
        all.Next(out _);
        for (var i = 2; i <= n; i++)
        {
            v = all.Next(out end);
            Check(v != null && v.Number == i && v.IsLast == (i == n) && end == GuideEnd.None, $"Enter goes to step {i}");
        }
        v = all.Back(out end);
        Check(v.Number == n - 1, "Back goes back one step");
        all.Next(out _);
        v = all.Next(out end);
        Check(v == null && end == GuideEnd.Finished && all.Mode == GuideMode.Off, "Enter on the last step finishes");
        Check(PlayerGuideFlow.CountsAsSeen(end), "finishing counts as seen");
        Check(all.Next(out end) == null && end == GuideEnd.None, "keys do nothing once it's off");

        all.Welcome();
        all.Skip(out end);
        Check(end == GuideEnd.Skipped && PlayerGuideFlow.CountsAsSeen(end), "Esc on the welcome skips, and counts as seen");

        all.Welcome();
        all.Next(out _);
        all.Next(out _);
        all.Skip(out end);
        Check(end == GuideEnd.Skipped && all.Mode == GuideMode.Off, "Esc mid-guide skips");

        all.Welcome();
        all.Next(out _);
        end = all.MenuClosed();
        Check(end == GuideEnd.MenuClosed && !PlayerGuideFlow.CountsAsSeen(end), "closing the menu mid-guide doesn't count as seen");
        Check(all.MenuClosed() == GuideEnd.None, "a closed menu with no guide up does nothing");

        v = all.StartTour(startedFromMenu: true);
        Check(v.Number == 1, "Start the guide: step 1");
        v = all.Back(out end);
        Check(v == null && end == GuideEnd.BackToGuideMenu, "from the Player Guide menu, Back on step 1 returns there");

        var q = content.Questions[3];
        v = all.StartQuestion(q);
        Check(v.Mode == GuideMode.Question && v.Card == q, "a question shows its card");
        v = all.Next(out end);
        Check(v == null && end == GuideEnd.QuestionDone && !PlayerGuideFlow.CountsAsSeen(end), "Enter on a question: done, on that page");
        all.StartQuestion(q);
        all.Back(out end);
        Check(end == GuideEnd.BackToQuestions, "Backspace on a question: back to the questions");
        all.StartQuestion(q);
        all.Skip(out end);
        Check(end == GuideEnd.QuestionDone, "Esc on a question is done too");

        // ---- Cards this player doesn't get are left out ----
        bool NoDepartmentOutfits(GuideCard c) => c.RequiresConvar != "vmenu_dept_outfits";
        var noDept = new PlayerGuideFlow(content, NoDepartmentOutfits);
        Check(noDept.AvailableSteps().All(s => s.Id != "uniforms") && noDept.AvailableSteps().Count == n - 1,
            "with Department Outfits off, its step is left out");
        Check(noDept.AvailableQuestions().All(c => c.RequiresConvar != "vmenu_dept_outfits"), "and so is its question");
        noDept.Welcome();
        v = noDept.Next(out _);
        Check(v.Count == n - 1, "the count shown matches the steps this player gets");

        var noSpawner = new PlayerGuideFlow(content, c => c.Menu != "vehicleSpawner");
        Check(noSpawner.AvailableSteps().All(s => s.Menu != "vehicleSpawner"), "a step whose menu the player lacks is left out");

        var none = new PlayerGuideFlow(content, _ => false);
        Check(none.StartTour(true) == null && none.Mode == GuideMode.Off, "no steps at all: the guide doesn't start");
        none.Welcome();
        Check(none.Next(out end) == null && end == GuideEnd.Finished, "and the welcome just closes");

        // ---- Keys ----
        GuideKey Route(bool esc = false, bool cancel = false, bool accept = false, bool x = false,
            bool left = false, bool right = false, bool pad = false) => GuideInput.Route(esc, cancel, accept, x, left, right, pad);
        Check(Route(esc: true, cancel: true) == GuideKey.Skip, "Esc (which is also the game's cancel) skips");
        Check(Route(cancel: true) == GuideKey.Back, "Backspace goes back");
        Check(Route(left: true) == GuideKey.Back, "← goes back");
        Check(Route(accept: true) == GuideKey.Next && Route(right: true) == GuideKey.Next, "Enter and → go on");
        Check(Route(x: true) == GuideKey.None, "Space on a keyboard does nothing");
        Check(Route(x: true, pad: true) == GuideKey.Skip, "X on a controller skips");
        Check(Route(cancel: true, pad: true) == GuideKey.Back && Route(accept: true, pad: true) == GuideKey.Next, "B and A");
        Check(Route() == GuideKey.None, "no key, nothing");

        // ---- Rows ----
        Check(GuideInput.PlainText("~r~Delete Vehicle") == "Delete Vehicle", "colour codes are dropped");
        Check(GuideInput.PlainText("~h~  Player Guide ") == "Player Guide", "padding is dropped");
        Check(GuideInput.ViewOffset(0, 1, 10, 7) == 0, "short menu: no scroll");
        Check(GuideInput.ViewOffset(4, 6, 10, 20) == 0, "rows already on screen: no scroll");
        Check(GuideInput.ViewOffset(12, 14, 10, 20) == 5, "rows lower down: scrolled until the last is on screen");
        Check(GuideInput.ViewOffset(2, 15, 10, 20) == 2, "rows too far apart: the first stays on screen");
        Check(GuideInput.ViewOffset(18, 19, 10, 20) == 10, "never past the end");

        // ---- The button hold: MenuAPI acts on a key's release ----
        var hold = new GuideButtonHold();
        Check(hold.ReadyToRelease(false, 0), "not holding: nothing to wait for");
        hold.Start(buttonsDisabledNow: false);
        Check(hold.Holding && !hold.ButtonsBefore, "the guide holds the buttons and remembers they were on");
        hold.Start(buttonsDisabledNow: true);
        Check(!hold.ButtonsBefore, "a second card doesn't overwrite what the menu had before the guide");
        Check(!hold.ReadyToRelease(true, 1000), "Enter still down: keep holding");
        Check(!hold.ReadyToRelease(true, 5000), "however long it's held");
        Check(!hold.ReadyToRelease(false, 5001), "just let go: keep holding");
        Check(!hold.ReadyToRelease(false, 5001 + GuideButtonHold.ReleaseDelayMs - 1), "until the delay has passed");
        Check(!hold.ReadyToRelease(true, 5200), "pressed again: the wait starts over");
        Check(!hold.ReadyToRelease(false, 5300), "let go again");
        Check(hold.ReadyToRelease(false, 5300 + GuideButtonHold.ReleaseDelayMs) && !hold.Holding, "then the buttons go back");
        hold.Start(buttonsDisabledNow: true);
        Check(hold.ButtonsBefore, "a menu that already had its buttons off gets them back off");
        Check(!hold.ReadyToRelease(false, 9000) && hold.ReadyToRelease(false, 9000 + GuideButtonHold.ReleaseDelayMs) && !hold.Holding,
            "no key held: the buttons go back after the short wait");

        // Another resource's on-screen keyboard during the guide. MenuAPI saves the buttons setting (off,
        // because of the guide) and writes it back 300 ms after the keyboard closes, so the hold must outlast that.
        Check(GuideButtonHold.KeyboardDelayMs >= 300 + 50, "the keyboard wait outlasts MenuAPI's 300 ms");
        var kb = new GuideButtonHold();
        kb.Start(buttonsDisabledNow: false);
        kb.NoteKeyboard(true, 1000);
        kb.NoteKeyboard(true, 2000);
        Check(!kb.ReadyToRelease(false, 2000), "a keyboard open: hold");
        kb.NoteKeyboard(false, 2100);
        Check(!kb.ReadyToRelease(false, 2000 + GuideButtonHold.ReleaseDelayMs), "the key wait alone isn't enough after a keyboard");
        Check(!kb.ReadyToRelease(false, 2000 + 300), "when MenuAPI writes its setting back, still holding");
        Check(kb.ReadyToRelease(false, 2000 + GuideButtonHold.KeyboardDelayMs) && !kb.Holding, "after that, the buttons go back");
        kb.Start(buttonsDisabledNow: false);
        Check(!kb.ReadyToRelease(false, 50000) && kb.ReadyToRelease(false, 50000 + GuideButtonHold.ReleaseDelayMs),
            "a later guide doesn't remember the old keyboard");

        Console.WriteLine($"player-guide: {checks} checks passed");
        return 0;
    }
}
