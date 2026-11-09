// A pretend game for the clothing resolver: per slot, an ordered list of collections, each a list
// of texture counts (one per drawable). Global numbers are positions in the joined list, exactly
// as FiveM documents GET_PED_DRAWABLE_GLOBAL_INDEX_FROM_COLLECTION.
using System;
using System.Collections.Generic;
using System.Linq;
using vMenuClient;

sealed class FakeRuntime : IClothingRuntime
{
    public FakeRuntime(uint model) => Model = model;

    public uint Model { get; }
    public IDictionary<string, object> Memo { get; } = new Dictionary<string, object>();
    public readonly Dictionary<string, List<KeyValuePair<string, int[]>>> Slots = new();

    public FakeRuntime Add(string slot, string collection, params int[] textures)
    {
        if (!Slots.TryGetValue(slot, out var list)) Slots[slot] = list = new List<KeyValuePair<string, int[]>>();
        list.Add(new KeyValuePair<string, int[]>(collection, textures));
        Memo.Clear();
        return this;
    }

    public FakeRuntime Fill(string slot, string collection, int drawables, int textures) => Add(slot, collection, Enumerable.Repeat(textures, drawables).ToArray());

    public FakeRuntime Clone()
    {
        var copy = new FakeRuntime(Model);
        foreach (var p in Slots) copy.Slots[p.Key] = p.Value.Select(s => new KeyValuePair<string, int[]>(s.Key, s.Value.ToArray())).ToList();
        return copy;
    }

    IEnumerable<KeyValuePair<string, int[]>> Of(ClothingSlot slot) => Slots.TryGetValue(slot.Key, out var l) ? l : Enumerable.Empty<KeyValuePair<string, int[]>>();

    public string LayoutPrint => ClothingPrints.Layout(slot => Of(slot).Select(s => new KeyValuePair<string, int>(s.Key, s.Value.Length)));

    public int ToGlobal(ClothingSlot slot, string collection, int local)
    {
        var offset = 0;
        foreach (var s in Of(slot))
        {
            if (s.Key == collection) return local >= 0 && local < s.Value.Length ? offset + local : -1;
            offset += s.Value.Length;
        }
        return -1;
    }

    public int TextureCount(ClothingSlot slot, string collection, int local)
    {
        var s = Of(slot).FirstOrDefault(x => x.Key == collection);
        return s.Value != null && local >= 0 && local < s.Value.Length ? s.Value[local] : 0;
    }

    public bool FromGlobal(ClothingSlot slot, int global, out string collection, out int local)
    {
        var offset = 0;
        foreach (var s in Of(slot))
        {
            if (global >= offset && global < offset + s.Value.Length)
            {
                collection = s.Key;
                local = global - offset;
                return true;
            }
            offset += s.Value.Length;
        }
        collection = null;
        local = -1;
        return false;
    }

    public int[] CollectionTextureCounts(string collection, ClothingSlot slot) => Of(slot).FirstOrDefault(x => x.Key == collection).Value ?? Array.Empty<int>();

    /// <summary>This game's numbering as a captured baseline.</summary>
    public ModelLayout ToBaseline()
    {
        var layout = new ModelLayout { Model = Model };
        foreach (var p in Slots)
        {
            var start = 0;
            var rows = new List<LayoutSegment>();
            foreach (var s in p.Value)
            {
                rows.Add(new LayoutSegment { Collection = s.Key, Start = start, Count = s.Value.Length, Textures = s.Value.ToArray() });
                start += s.Value.Length;
            }
            layout.Slots[p.Key] = rows;
        }
        return layout;
    }

    /// <summary>Texture counts of the named collections, as a pack revision would list them.</summary>
    public PackRevision ToRevision(string id, params string[] collections)
    {
        var revision = new PackRevision { Id = id };
        var counts = new Dictionary<string, int[]>();
        foreach (var c in collections)
        {
            revision.Collections.Add(c);
            foreach (var slot in ClothingSlot.AllLayoutSlots)
            {
                var values = CollectionTextureCounts(c, slot);
                if (values.Length > 0) counts[PackRevision.CountKey(c, slot)] = values;
            }
        }
        revision.Counts[Model] = counts;
        return revision;
    }
}
