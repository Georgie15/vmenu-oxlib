using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace vMenuClient
{
    /// <summary>
    /// One card of the Player Guide: the menu page it opens, the rows it lights up and what it says.
    /// The text comes from config/player-guide.json. Body, tip and foot take two marks: **bold** and
    /// [KEY] for a key cap; everything else is shown as plain text.
    /// </summary>
    public sealed class GuideCard
    {
        public string Id = "";
        public string Menu = "main";
        public List<string> Highlight = new();
        public string Title = "";
        public List<string> Body = new();
        public string Tip;
        public string Foot;
        /// <summary>A convar that must be "true" for this card to show (e.g. vmenu_dept_outfits).</summary>
        public string RequiresConvar;
    }

    /// <summary>The Player Guide's content, read from config/player-guide.json.</summary>
    public sealed class GuideContent
    {
        /// <summary>The menu pages a card can open. PlayerGuide maps each one to the real menu.</summary>
        public static readonly string[] MenuIds =
        {
            "main", "guide", "player", "vehicle", "playerOptions", "appearance", "mpPed",
            "vehicleOptions", "vehicleSpawner", "savedVehicles", "personalVehicle", "misc"
        };

        public int Version = 1;
        public GuideCard Welcome;
        public List<GuideCard> Steps = new();
        /// <summary>The "How do I…?" questions. A question's Title is the question itself.</summary>
        public List<GuideCard> Questions = new();

        private static readonly string[] CardKeys = { "id", "menu", "highlight", "title", "body", "tip", "foot", "requiresConvar" };
        private static readonly string[] QuestionKeys = { "question", "step", "menu", "highlight", "body", "tip", "requiresConvar" };

        /// <summary>
        /// Reads the guide. Returns null and says why in <paramref name="error"/> when the file is
        /// missing or wrong in any way, so a typo can't show a half-broken guide.
        /// </summary>
        public static GuideContent Parse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "the file is missing or empty";
                return null;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception e)
            {
                error = "it isn't valid JSON: " + e.Message;
                return null;
            }

            try
            {
                var content = new GuideContent();
                Unknown(root, new[] { "version", "welcome", "steps", "questions" }, "the file");

                if (root["version"] is JToken v)
                {
                    if (v.Type != JTokenType.Integer || (int)v < 1)
                    {
                        throw new FormatException("\"version\" must be a whole number of 1 or more");
                    }
                    content.Version = (int)v;
                }

                if (root["welcome"] is not JObject welcome)
                {
                    throw new FormatException("\"welcome\" is missing");
                }
                content.Welcome = ReadCard(welcome, "welcome", requireId: false);

                if (root["steps"] is not JArray steps || steps.Count == 0)
                {
                    throw new FormatException("\"steps\" must list at least one step");
                }
                for (var i = 0; i < steps.Count; i++)
                {
                    if (steps[i] is not JObject step)
                    {
                        throw new FormatException($"step {i + 1} isn't an object");
                    }
                    var card = ReadCard(step, $"step {i + 1}", requireId: true);
                    if (content.Steps.Any(s => s.Id == card.Id))
                    {
                        throw new FormatException($"two steps have the id \"{card.Id}\"");
                    }
                    content.Steps.Add(card);
                }

                if (root["questions"] is JToken qs)
                {
                    if (qs is not JArray questions)
                    {
                        throw new FormatException("\"questions\" must be a list");
                    }
                    for (var i = 0; i < questions.Count; i++)
                    {
                        if (questions[i] is not JObject q)
                        {
                            throw new FormatException($"question {i + 1} isn't an object");
                        }
                        content.Questions.Add(ReadQuestion(q, $"question {i + 1}", content.Steps));
                    }
                }

                return content;
            }
            catch (FormatException e)
            {
                error = e.Message;
                return null;
            }
        }

        private static GuideCard ReadCard(JObject o, string where, bool requireId)
        {
            Unknown(o, CardKeys, where);
            var card = new GuideCard
            {
                Id = Text(o, "id", where, required: requireId) ?? "",
                Menu = Text(o, "menu", where, required: false) ?? "main",
                Title = Text(o, "title", where, required: true),
                Body = Lines(o, where),
                Highlight = Labels(o, where),
                Tip = Text(o, "tip", where, required: false),
                Foot = Text(o, "foot", where, required: false),
                RequiresConvar = Text(o, "requiresConvar", where, required: false),
            };
            if (!MenuIds.Contains(card.Menu))
            {
                throw new FormatException($"{where}: unknown menu \"{card.Menu}\" (use one of {string.Join(", ", MenuIds)})");
            }
            return card;
        }

        private static GuideCard ReadQuestion(JObject o, string where, List<GuideCard> steps)
        {
            Unknown(o, QuestionKeys, where);
            var question = Text(o, "question", where, required: true);
            var stepId = Text(o, "step", where, required: false);
            if (stepId != null)
            {
                foreach (var key in new[] { "menu", "highlight", "body", "tip" })
                {
                    if (o[key] != null)
                    {
                        throw new FormatException($"{where}: give either \"step\" or its own \"{key}\", not both");
                    }
                }
                var step = steps.FirstOrDefault(s => s.Id == stepId)
                    ?? throw new FormatException($"{where}: there is no step \"{stepId}\"");
                return new GuideCard
                {
                    Id = step.Id,
                    Menu = step.Menu,
                    Highlight = step.Highlight.ToList(),
                    Title = question,
                    Body = step.Body.ToList(),
                    Tip = step.Tip,
                    RequiresConvar = Text(o, "requiresConvar", where, required: false) ?? step.RequiresConvar,
                };
            }

            var card = new GuideCard
            {
                Menu = Text(o, "menu", where, required: false) ?? "main",
                Title = question,
                Body = Lines(o, where),
                Highlight = Labels(o, where),
                Tip = Text(o, "tip", where, required: false),
                RequiresConvar = Text(o, "requiresConvar", where, required: false),
            };
            if (!MenuIds.Contains(card.Menu))
            {
                throw new FormatException($"{where}: unknown menu \"{card.Menu}\" (use one of {string.Join(", ", MenuIds)})");
            }
            return card;
        }

        private static void Unknown(JObject o, string[] allowed, string where)
        {
            foreach (var p in o.Properties())
            {
                if (!allowed.Contains(p.Name))
                {
                    throw new FormatException($"{where}: unknown setting \"{p.Name}\"");
                }
            }
        }

        private static string Text(JObject o, string key, string where, bool required)
        {
            var token = o[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (required)
                {
                    throw new FormatException($"{where}: \"{key}\" is missing");
                }
                return null;
            }
            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
            {
                throw new FormatException($"{where}: \"{key}\" must be text");
            }
            return ((string)token).Trim();
        }

        private static List<string> Lines(JObject o, string where)
        {
            if (o["body"] is not JArray body || body.Count == 0)
            {
                throw new FormatException($"{where}: \"body\" must list at least one paragraph");
            }
            var lines = new List<string>();
            foreach (var line in body)
            {
                if (line.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)line))
                {
                    throw new FormatException($"{where}: every \"body\" paragraph must be text");
                }
                lines.Add(((string)line).Trim());
            }
            return lines;
        }

        private static List<string> Labels(JObject o, string where)
        {
            var token = o["highlight"];
            if (token == null)
            {
                return new List<string>();
            }
            if (token is not JArray list)
            {
                throw new FormatException($"{where}: \"highlight\" must be a list of menu row names");
            }
            var labels = new List<string>();
            foreach (var label in list)
            {
                if (label.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)label))
                {
                    throw new FormatException($"{where}: every \"highlight\" entry must be a menu row name");
                }
                labels.Add(((string)label).Trim());
            }
            return labels;
        }
    }

    public enum GuideKey { None, Next, Back, Skip }

    /// <summary>Which guide key a frame's presses mean, and the menu rows a card points at.</summary>
    public static class GuideInput
    {
        /// <summary>
        /// Esc also counts as the game's cancel control, so it is checked first: Esc skips and
        /// Backspace goes back. X skips only on a controller, since on a keyboard it's Space.
        /// </summary>
        public static GuideKey Route(bool esc, bool cancel, bool accept, bool x, bool left, bool right, bool pad)
        {
            if (esc || (pad && x))
            {
                return GuideKey.Skip;
            }
            if (cancel || left)
            {
                return GuideKey.Back;
            }
            if (accept || right)
            {
                return GuideKey.Next;
            }
            return GuideKey.None;
        }

        /// <summary>A row's text as the menu shows it: no ~colour~ codes, no padding.</summary>
        public static string PlainText(string text) =>
            System.Text.RegularExpressions.Regex.Replace(text ?? "", "~[a-zA-Z0-9_]+~", "").Replace('\u00A0', ' ').Trim();

        /// <summary>The scroll that keeps rows first..last on screen (first wins when they don't all fit).</summary>
        public static int ViewOffset(int first, int last, int maxOnScreen, int count)
        {
            var offset = Math.Max(0, last - maxOnScreen + 1);
            offset = Math.Min(offset, first);
            return Math.Max(0, Math.Min(offset, count - maxOnScreen));
        }
    }

    /// <summary>
    /// Keeps the menu's own buttons off while the guide is up, and after it ends until the key that
    /// ended it has been let go for <see cref="ReleaseDelayMs"/>. MenuAPI acts on a key's release, so
    /// giving the buttons back while Enter is still down would also pick the selected row.
    ///
    /// It also waits out an on-screen keyboard (another resource's): MenuAPI saves the buttons setting
    /// when one opens and writes it back 300 ms after it closes. Giving the buttons back before that
    /// would let MenuAPI put back the guide's "off", for good.
    /// </summary>
    public sealed class GuideButtonHold
    {
        public const int ReleaseDelayMs = 150;
        public const int KeyboardDelayMs = 400;
        private int releasedSince = -1;
        private int keyboardSeenAt = -1;

        /// <summary>From the first card until the menu has its buttons back.</summary>
        public bool Holding { get; private set; }
        /// <summary>The menu's own setting before the guide, put back at the end.</summary>
        public bool ButtonsBefore { get; private set; }

        public void Start(bool buttonsDisabledNow)
        {
            if (!Holding)
            {
                ButtonsBefore = buttonsDisabledNow;
                Holding = true;
            }
            releasedSince = -1;
        }

        /// <summary>Every frame while holding: whether an on-screen keyboard is open.</summary>
        public void NoteKeyboard(bool open, int now)
        {
            if (open)
            {
                keyboardSeenAt = now;
            }
        }

        /// <summary>After the guide ends, each frame: true once the buttons can go back to <see cref="ButtonsBefore"/>.</summary>
        public bool ReadyToRelease(bool anyKeyHeld, int now)
        {
            if (!Holding)
            {
                return true;
            }
            if (anyKeyHeld)
            {
                releasedSince = -1;
                return false;
            }
            if (releasedSince < 0)
            {
                releasedSince = now;
            }
            if (now - releasedSince < ReleaseDelayMs)
            {
                return false;
            }
            if (keyboardSeenAt >= 0 && now - keyboardSeenAt < KeyboardDelayMs)
            {
                return false;
            }
            Holding = false;
            keyboardSeenAt = -1;
            return true;
        }
    }

    public enum GuideMode { Off, Welcome, Tour, Question }

    /// <summary>How the guide ended, which decides where the menu is left and whether it counts as seen.</summary>
    public enum GuideEnd
    {
        None,
        /// <summary>Enter on the last step. Seen.</summary>
        Finished,
        /// <summary>Esc (X on a controller). Seen; the menu stays on the page shown.</summary>
        Skipped,
        /// <summary>The menu closed under the guide. Not seen, so the welcome comes back next time.</summary>
        MenuClosed,
        /// <summary>Back from the first step of a guide started from the Player Guide menu.</summary>
        BackToGuideMenu,
        /// <summary>Enter on a question: the menu stays on the page it pointed at.</summary>
        QuestionDone,
        /// <summary>Backspace on a question: back to the list of questions.</summary>
        BackToQuestions,
    }

    /// <summary>What the guide card shows now.</summary>
    public sealed class GuideView
    {
        public GuideMode Mode;
        public GuideCard Card;
        /// <summary>1-based step number and the number of steps, for the tour.</summary>
        public int Number;
        public int Count;
        public bool IsLast;
    }

    /// <summary>
    /// The guide's steps and keys, with no game calls, so it can be tested on its own. PlayerGuide
    /// turns each view into a menu page and the card, and each end into where the menu is left.
    /// </summary>
    public sealed class PlayerGuideFlow
    {
        private readonly GuideContent content;
        private readonly Func<GuideCard, bool> available;
        private List<GuideCard> tour = new();
        private bool fromMenu;

        public GuideMode Mode { get; private set; } = GuideMode.Off;
        public int StepIndex { get; private set; }
        public GuideCard Question { get; private set; }

        /// <param name="available">Whether a card can show for this player: its menu exists for them and its convar is on.</param>
        public PlayerGuideFlow(GuideContent content, Func<GuideCard, bool> available)
        {
            this.content = content;
            this.available = available;
        }

        /// <summary>The steps this player gets, in order.</summary>
        public List<GuideCard> AvailableSteps() => content.Steps.Where(available).ToList();

        /// <summary>The questions this player gets, in order.</summary>
        public List<GuideCard> AvailableQuestions() => content.Questions.Where(available).ToList();

        /// <summary>The welcome card, shown the first time the menu opens.</summary>
        public GuideView Welcome()
        {
            tour = AvailableSteps();
            fromMenu = false;
            Mode = GuideMode.Welcome;
            return View();
        }

        /// <summary>The first step. From the Player Guide menu, Back on it returns there.</summary>
        public GuideView StartTour(bool startedFromMenu)
        {
            tour = AvailableSteps();
            fromMenu = startedFromMenu;
            if (tour.Count == 0)
            {
                Mode = GuideMode.Off;
                return null;
            }
            Mode = GuideMode.Tour;
            StepIndex = 0;
            return View();
        }

        public GuideView StartQuestion(GuideCard question)
        {
            Mode = GuideMode.Question;
            Question = question;
            return View();
        }

        /// <summary>Enter. Returns the next view, or null with how it ended.</summary>
        public GuideView Next(out GuideEnd end)
        {
            end = GuideEnd.None;
            switch (Mode)
            {
                case GuideMode.Welcome:
                    if (tour.Count == 0)
                    {
                        return Stop(GuideEnd.Finished, out end);
                    }
                    Mode = GuideMode.Tour;
                    StepIndex = 0;
                    return View();
                case GuideMode.Tour:
                    if (StepIndex + 1 >= tour.Count)
                    {
                        return Stop(GuideEnd.Finished, out end);
                    }
                    StepIndex++;
                    return View();
                case GuideMode.Question:
                    return Stop(GuideEnd.QuestionDone, out end);
                default:
                    return null;
            }
        }

        /// <summary>Backspace.</summary>
        public GuideView Back(out GuideEnd end)
        {
            end = GuideEnd.None;
            switch (Mode)
            {
                case GuideMode.Welcome:
                    return View(); // nothing before the welcome
                case GuideMode.Tour:
                    if (StepIndex > 0)
                    {
                        StepIndex--;
                        return View();
                    }
                    if (fromMenu)
                    {
                        return Stop(GuideEnd.BackToGuideMenu, out end);
                    }
                    Mode = GuideMode.Welcome;
                    return View();
                case GuideMode.Question:
                    return Stop(GuideEnd.BackToQuestions, out end);
                default:
                    return null;
            }
        }

        /// <summary>Esc, or X on a controller.</summary>
        public GuideView Skip(out GuideEnd end)
        {
            end = GuideEnd.None;
            switch (Mode)
            {
                case GuideMode.Welcome:
                case GuideMode.Tour:
                    return Stop(GuideEnd.Skipped, out end);
                case GuideMode.Question:
                    return Stop(GuideEnd.QuestionDone, out end);
                default:
                    return null;
            }
        }

        /// <summary>The menu closed under the guide (F1, death, another script).</summary>
        public GuideEnd MenuClosed()
        {
            if (Mode == GuideMode.Off)
            {
                return GuideEnd.None;
            }
            Stop(GuideEnd.MenuClosed, out var end);
            return end;
        }

        /// <summary>Whether an end counts as having seen the guide.</summary>
        public static bool CountsAsSeen(GuideEnd end) => end == GuideEnd.Finished || end == GuideEnd.Skipped;

        /// <summary>The view for the current state, or null while off.</summary>
        public GuideView View()
        {
            switch (Mode)
            {
                case GuideMode.Welcome:
                    return new GuideView { Mode = Mode, Card = content.Welcome };
                case GuideMode.Tour:
                    return new GuideView
                    {
                        Mode = Mode,
                        Card = tour[StepIndex],
                        Number = StepIndex + 1,
                        Count = tour.Count,
                        IsLast = StepIndex == tour.Count - 1,
                    };
                case GuideMode.Question:
                    return new GuideView { Mode = Mode, Card = Question };
                default:
                    return null;
            }
        }

        private GuideView Stop(GuideEnd how, out GuideEnd end)
        {
            Mode = GuideMode.Off;
            end = how;
            return null;
        }
    }
}
