using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using MenuAPI;
using vMenuClient.menus;

static class Program
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    static void Main()
    {
        var menu = new Menu();
        var editor = new FaceSelectionMenu(menu);
        var legacy = new PedHeadBlendData(43, 21, 0, 12, 8, 0, .37f, .64f, 0f, true);
        API.Data = legacy;
        editor.Refresh();
        Check(API.Writes == 0 && API.Data == legacy, "Opening a legacy blend must be read-only.");
        Check(menu.List("Face").ListItems.Count == 47, "Without ONX only 46 vanilla faces plus keep-current.");
        Check(menu.List("Face").ListIndex == 0, "Legacy blend stays custom.");
        Check(menu.List("Blend Face A").ListIndex == 43, "IDs must not be reordered parent-list indices.");

        API.ResourceState = "started";
        editor.Refresh();
        Check(menu.List("Face").ListItems.Count == 99, "All 98 faces available when ONX runs.");
        Check(menu.List("Face").ListItems[47] == "Custom #46", "First addon face has exact ID.");
        menu.Change("Face", 98);
        Check(API.Data.FirstFaceShape == 97 && API.Data.SecondFaceShape == 97 && API.Data.ThirdFaceShape == 97,
            "Last ONX face applies to every shape input.");
        Check(API.Data.FirstSkinTone == 12 && API.Data.SecondSkinTone == 8 && API.Data.ParentSkinTonePercent == .64f,
            "Face selection must preserve the existing skin blend.");
        Check(menu.List("Blend Face A").ListIndex == 97, "Advanced controls track direct selection.");
        menu.Change("Skin Tone", 6);
        Check(API.Data.FirstSkinTone == 5 && API.Data.SecondSkinTone == 5 && API.Data.FirstFaceShape == 97,
            "Direct skin selection must preserve the ONX face.");

        menu.Change("Blend Face B", 46);
        menu.Slide("Face Blend", 7);
        Check(API.Data.FirstFaceShape == 97 && API.Data.SecondFaceShape == 46 && API.Data.ParentFaceShapePercent == .7f,
            "Blending uses the event's new value, not the slider's old position.");
        Check(menu.Slider("Face Blend").Position == 7, "MenuAPI's post-event update must not advance the slider twice.");
        Check(menu.List("Face").ListIndex == 0, "Mixed faces display keep-current.");
        int writes = API.Writes;
        menu.Change("Face", 0);
        Check(API.Writes == writes, "Selecting keep-current is non-destructive.");
        Check(menu.List("Face").ListIndex == 0, "Keep-current must remain selectable so list navigation can wrap.");

        // Simulate persistence and opening a fresh editor with the restored native data.
        var saved = JsonSerializer.Serialize(API.Data);
        API.Data = default;
        API.Data = JsonSerializer.Deserialize<PedHeadBlendData>(saved);
        var reopened = new FaceSelectionMenu(menu = new Menu());
        reopened.Refresh();
        Check(API.Writes == writes && API.Data.FirstFaceShape == 97 && API.Data.SecondFaceShape == 46,
            "Saved addon IDs survive reopening without an implicit apply.");
        Check(menu.List("Blend Face A").ListIndex == 97 && menu.List("Blend Face B").ListIndex == 46,
            "Reopened controls display saved IDs.");

        API.ResourceState = "stopped";
        menu.Change("Face", 47);
        Check(API.Writes == writes, "Reject stale addon selection if resource stops with menu open.");
        reopened.Refresh();
        Check(API.Data.FirstFaceShape == 97 && API.Writes == writes, "Missing pack must not rewrite a saved face.");
        Check(menu.List("Blend Face A").ListItems.Last() == "Saved #97 (unavailable)", "Unavailable saved ID remains visible.");
        menu.Change("Face", 1);
        Check(API.Data.FirstFaceShape == 0 && API.Data.FirstSkinTone == 5, "Vanilla selection still works after stop.");
        API.ResourceState = "started";
        reopened.Refresh();
        Check(menu.List("Face").ListItems.Count == 99, "Restarted pack discovered on reopen.");

        API.Data = new PedHeadBlendData(1, 2, 3, 4, 5, 6, .2f, .8f, .3f, false);
        reopened.Refresh();
        menu.Change("Face", 47);
        Check(API.Data.FirstFaceShape == 46 && API.Data.ThirdFaceShape == 46 && API.Data.ParentThirdUnkPercent == .3f
            && API.Data.ThirdSkinTone == 6, "Direct face preserves all three skin inputs and third weight.");
        menu.Change("Skin Tone", 10);
        Check(API.Data.ThirdSkinTone == 9 && API.Data.ThirdFaceShape == 46, "Direct skin preserves face inputs.");

        foreach (bool male in new[] { true, false })
        {
            reopened.Reset(male);
            Check(API.Data.FirstFaceShape == (male ? 0 : 21) && API.Data.ParentThirdUnkPercent == 0,
                "New characters get fresh gender-appropriate defaults.");
        }
        var random = new Random(123);
        bool sawAddon = false;
        foreach (string state in new[] { "missing", "starting", "stopped", "started" })
        {
            API.ResourceState = state;
            for (int i = 0; i < 200; i++)
            {
                reopened.Randomize(random);
                Check(API.Data.FirstFaceShape <= (state == "started" ? 97 : 45), "Randomization respects availability.");
                Check(API.Data.FirstSkinTone < 46 && API.Data.SecondSkinTone < 46, "Never use addon face IDs as skin IDs.");
                sawAddon |= API.Data.FirstFaceShape >= 46;
            }
        }
        Check(sawAddon, "Randomization includes addon faces.");
        Console.WriteLine("PASS: resource lifecycle, direct face/skin, blending, saved IDs, third blend, defaults, randomization.");
    }
}

namespace CitizenFX.Core
{
    public readonly record struct PedHeadBlendData(int FirstFaceShape, int SecondFaceShape, int ThirdFaceShape,
        int FirstSkinTone, int SecondSkinTone, int ThirdSkinTone, float ParentFaceShapePercent,
        float ParentSkinTonePercent, float ParentThirdUnkPercent, bool IsParentInheritance);
    public class Ped
    {
        public int Handle => 1;
        public PedHeadBlendData GetHeadBlendData() => Native.API.Data;
    }
    public static class Game { public static Ped PlayerPed = new Ped(); }
}

namespace CitizenFX.Core.Native
{
    public static class API
    {
        public static PedHeadBlendData Data;
        public static int Writes;
        public static string ResourceState = "missing";
        public static string GetResourceState(string name) => name == "cfx_onx_mp_faces" ? ResourceState : "missing";
        public static void SetPedHeadBlendData(int ped, int a, int b, int c, int sa, int sb, int sc,
            float mix, float skin, float third, bool parent)
        {
            Writes++;
            Data = new PedHeadBlendData(a, b, c, sa, sb, sc, mix, skin, third, parent);
        }
    }
}

namespace MenuAPI
{
    public class MenuItem { public string Text; }
    public class MenuListItem : MenuItem
    {
        public List<string> ListItems;
        private int index;
        public int ListIndex
        {
            get => index;
            set { if (value < 0 || value >= ListItems.Count) throw new Exception("Invalid list index"); index = value; }
        }
        public MenuListItem(string title, List<string> labels, int selected, string description)
        { Text = title; ListItems = labels; ListIndex = selected; }
    }
    public class MenuSliderItem : MenuItem
    {
        public int Position;
        public MenuSliderItem(string title, string description, int min, int max, int position, bool show)
        { Text = title; Position = position; }
    }
    public class Menu
    {
        public event Action<Menu, MenuListItem, int, int, int> OnListIndexChange;
        public event Action<Menu, MenuSliderItem, int, int, int> OnSliderPositionChange;
        private readonly List<MenuItem> items = new List<MenuItem>();
        public void ClearMenuItems() => items.Clear();
        public void AddMenuItem(MenuItem item) => items.Add(item);
        public void RefreshIndex() { }
        public MenuListItem List(string name) => (MenuListItem)items.Single(i => i.Text == name);
        public MenuSliderItem Slider(string name) => (MenuSliderItem)items.Single(i => i.Text == name);
        public void Change(string name, int index)
        {
            var item = List(name);
            int old = item.ListIndex;
            item.ListIndex = index;
            OnListIndexChange?.Invoke(this, item, old, index, items.IndexOf(item));
        }
        public void Slide(string name, int position)
        {
            var item = (MenuSliderItem)items.Single(i => i.Text == name);
            int delta = position - item.Position;
            OnSliderPositionChange?.Invoke(this, item, item.Position, position, items.IndexOf(item));
            item.Position += delta;
        }
    }
}
