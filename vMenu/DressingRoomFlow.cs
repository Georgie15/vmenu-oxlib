using System;
using System.Collections.Generic;
using System.Linq;

namespace vMenuClient
{
    public enum DressingPhase { Off, Steps, Finish }

    /// <summary>One line of the dressing room's checklist.</summary>
    public sealed class DressingCheck
    {
        public string Title;
        public bool Done;
        public bool Current;
    }

    /// <summary>What the dressing room card shows now.</summary>
    public sealed class DressingView
    {
        public DressingPhase Phase;
        public string Title = "";
        public List<string> Body = new();
        public string Tip;
        public string Foot;
        /// <summary>Rows to light up: only on the page the step points at.</summary>
        public List<string> Highlight = new();
        /// <summary>1-based number of the step the card is about, and how many there are.</summary>
        public int Number;
        public int Count;
        public List<DressingCheck> Checklist = new();
    }

    /// <summary>
    /// The dressing room's steps (psrp_dressing_room): the player makes their first character in
    /// MP Ped Customization while a card beside the menu says what to do next. Unlike the Player
    /// Guide's tour, the menu stays theirs; a step is ticked off by doing it: its row's page opens,
    /// they visit a page and come back, or the character is saved. Then one card, with its own keys,
    /// sends them on. No game calls, so it can be tested; DressingRoomGuide feeds it the open page.
    /// </summary>
    public sealed class DressingRoomFlow
    {
        /// <summary>The page id while no menu is open.</summary>
        public const string Closed = "";
        /// <summary>The page id of any menu outside MP Ped Customization and its creator.</summary>
        public const string Elsewhere = "other";

        private readonly DressingContent content;
        private readonly bool[] done;
        private readonly bool[] visiting;
        private bool inCreator;

        public DressingPhase Phase { get; private set; } = DressingPhase.Off;
        /// <summary>The page open now.</summary>
        public string Page { get; private set; } = Closed;
        /// <summary>Where the player goes after the dressing room, for the finish card.</summary>
        public string Destination { get; private set; } = "";

        public DressingRoomFlow(DressingContent content)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            done = new bool[content.Steps.Count];
            visiting = new bool[content.Steps.Count];
        }

        public static bool IsCreator(string page) => GuideContent.CreatorPages.Contains(page);

        /// <summary>A page the dressing room happens on: MP Ped Customization or the creator.</summary>
        public static bool IsDressingPage(string page) => page == "mpPed" || IsCreator(page);

        public void Start(string destination)
        {
            Array.Clear(done, 0, done.Length);
            Array.Clear(visiting, 0, visiting.Length);
            inCreator = false;
            Page = Closed;
            Destination = string.IsNullOrWhiteSpace(destination) ? "your spawn" : destination.Trim();
            Phase = DressingPhase.Steps;
        }

        public void Stop() => Phase = DressingPhase.Off;

        /// <summary>The first step not done yet (the save step once all others are).</summary>
        public int CurrentIndex
        {
            get
            {
                for (var i = 0; i < done.Length; i++)
                {
                    if (!done[i])
                    {
                        return i;
                    }
                }
                return done.Length - 1;
            }
        }

        public DressingStep CurrentStep => content.Steps[CurrentIndex];

        public bool IsDone(int index) => done[index];

        /// <summary>
        /// The page open now (<see cref="Closed"/>, <see cref="Elsewhere"/> or a menu id). Returns
        /// whether the card may need to change.
        /// </summary>
        public bool Update(string page)
        {
            page ??= Closed;
            if (Phase == DressingPhase.Off || page == Page)
            {
                return false;
            }
            Page = page;
            if (Phase != DressingPhase.Steps || page == Closed)
            {
                return true;
            }

            if (IsCreator(page))
            {
                inCreator = true;
            }
            else if (inCreator)
            {
                // Out of the creator without saving (Exit Without Saving): it starts over next time.
                inCreator = false;
                Array.Clear(done, 0, done.Length);
                Array.Clear(visiting, 0, visiting.Length);
            }

            for (var i = 0; i < content.Steps.Count; i++)
            {
                var step = content.Steps[i];
                if (step.Done == DressingDone.Open && page == step.Opens)
                {
                    done[i] = true;
                }
                else if (step.Done == DressingDone.Visit)
                {
                    if (page == step.Opens)
                    {
                        visiting[i] = true;
                    }
                    else if (visiting[i] && page == step.Card.Menu)
                    {
                        visiting[i] = false;
                        done[i] = true;
                    }
                }
            }
            return true;
        }

        /// <summary>The character was saved: every step is done, and the finish card is up.</summary>
        public bool Saved()
        {
            if (Phase != DressingPhase.Steps)
            {
                return false;
            }
            for (var i = 0; i < done.Length; i++)
            {
                done[i] = true;
            }
            Phase = DressingPhase.Finish;
            return true;
        }

        /// <summary>Enter on the finish card: off to <see cref="Destination"/>.</summary>
        public bool Leave()
        {
            if (Phase != DressingPhase.Finish)
            {
                return false;
            }
            Phase = DressingPhase.Off;
            return true;
        }

        /// <summary>Backspace on the finish card, or the menu closed under it: they keep the menu.</summary>
        public bool Stay()
        {
            if (Phase != DressingPhase.Finish)
            {
                return false;
            }
            Phase = DressingPhase.Off;
            return true;
        }

        /// <summary>The card for the page open now, or null when there is none to show.</summary>
        public DressingView View()
        {
            if (Phase == DressingPhase.Off || Page == Closed)
            {
                return null;
            }
            var count = content.Steps.Count;
            var current = CurrentIndex;
            var view = new DressingView
            {
                Phase = Phase,
                Count = count,
                Checklist = content.Steps.Select((s, i) => new DressingCheck
                {
                    Title = s.Card.Title,
                    Done = done[i],
                    Current = Phase == DressingPhase.Steps && i == current,
                }).ToList(),
            };

            if (Phase == DressingPhase.Finish)
            {
                var finish = content.Finish;
                view.Title = Fill(finish.Title);
                view.Body = finish.Body.Select(Fill).ToList();
                view.Tip = finish.Tip == null ? null : Fill(finish.Tip);
                view.Number = count;
                if (Page == finish.Menu)
                {
                    view.Highlight = finish.Highlight.ToList();
                }
                return view;
            }

            // Inside a page a visit step opens: that step's own help for the page.
            var inside = content.Steps.FindIndex(s => s.Done == DressingDone.Visit && s.Opens == Page);
            var shown = inside >= 0 ? inside : current;
            var step = content.Steps[shown];
            view.Number = shown + 1;
            view.Title = step.Card.Title;
            view.Tip = step.Card.Tip;
            if (inside >= 0)
            {
                view.Body = (step.Inside.Count > 0 ? step.Inside : step.Card.Body).ToList();
                if (done[inside])
                {
                    view.Foot = BackToCreator;
                }
                return view;
            }

            view.Body = step.Card.Body.ToList();
            if (Page == step.Card.Menu)
            {
                view.Highlight = step.Card.Highlight.ToList();
            }
            else if (IsCreator(Page))
            {
                view.Foot = BackToCreator;
            }
            else
            {
                view.Foot = BackToCustomization;
            }
            return view;
        }

        internal const string BackToCreator = "When you're happy, [BKSP] goes back to the creator.";
        internal const string BackToCustomization = "Go back to **MP Ped Customization** to carry on.";

        private string Fill(string text) => text?.Replace("{destination}", Destination);
    }
}
