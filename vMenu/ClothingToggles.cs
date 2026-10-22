using System.Collections.Generic;

namespace vMenuClient
{
    /// <summary>
    /// Player Appearance > Clothing Quick Toggles: take off a hat, glasses, ear accessory, mask or bag
    /// and later put back exactly what you took off. This class only decides; PlayerAppearance reads
    /// and sets the ped. What was taken off is remembered per ped, so a new ped (model change, MP
    /// character load, respawn as another ped) starts fresh. Kept free of game calls so it can be tested.
    /// </summary>
    internal sealed class ClothingToggles
    {
        internal enum Slot { Hat, Glasses, Ears, Mask, Bag }

        internal enum Outcome { TakenOff, PutBack, NothingToToggle }

        internal struct Look
        {
            public int Drawable;
            public int Texture;

            public Look(int drawable, int texture)
            {
                Drawable = drawable;
                Texture = texture;
            }
        }

        internal sealed class SlotInfo
        {
            public Slot Slot;
            public string Name;
            // For messages: "You aren't wearing a hat."
            public string Phrase;
            // A prop (ClearPedProp / SetPedPropIndex) or a component (drawable 0 = nothing).
            public bool IsProp;
            public int Index;
            // Components mean something else on non-MP peds, so only MP characters toggle them.
            public bool MpOnly => !IsProp;
        }

        internal static readonly IReadOnlyList<SlotInfo> Slots = new[]
        {
            new SlotInfo { Slot = Slot.Hat, Name = "Hat", Phrase = "a hat", IsProp = true, Index = 0 },
            new SlotInfo { Slot = Slot.Glasses, Name = "Glasses", Phrase = "glasses", IsProp = true, Index = 1 },
            new SlotInfo { Slot = Slot.Ears, Name = "Ear Accessory", Phrase = "an ear accessory", IsProp = true, Index = 2 },
            new SlotInfo { Slot = Slot.Mask, Name = "Mask", Phrase = "a mask", IsProp = false, Index = 1 },
            new SlotInfo { Slot = Slot.Bag, Name = "Bag", Phrase = "a bag", IsProp = false, Index = 5 },
        };

        private readonly Dictionary<Slot, Look> removed = new Dictionary<Slot, Look>();
        private int ped;

        /// <summary>
        /// Toggles a slot. <paramref name="worn"/> is what the slot holds now, or null if it's empty.
        /// Taking off remembers it; toggling an empty slot puts back what was taken off, if anything.
        /// </summary>
        internal Outcome Toggle(int ped, Slot slot, Look? worn, out Look putBack)
        {
            putBack = default;
            if (ped != this.ped)
            {
                removed.Clear();
                this.ped = ped;
            }
            if (worn.HasValue)
            {
                removed[slot] = worn.Value;
                return Outcome.TakenOff;
            }
            if (removed.TryGetValue(slot, out putBack))
            {
                removed.Remove(slot);
                return Outcome.PutBack;
            }
            return Outcome.NothingToToggle;
        }
    }
}
