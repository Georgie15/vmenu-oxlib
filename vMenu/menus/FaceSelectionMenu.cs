using System;
using System.Collections.Generic;
using System.Linq;

using CitizenFX.Core;
using MenuAPI;

using static CitizenFX.Core.Native.API;

namespace vMenuClient.menus
{
    // Reads the live head blend whenever opened. Opening the editor never changes a saved face.
    internal sealed class FaceSelectionMenu
    {
        private readonly Menu menu;
        private readonly Dictionary<MenuListItem, List<int>> ids = new Dictionary<MenuListItem, List<int>>();
        private MenuListItem face, skin, faceA, faceB, skinA, skinB;
        private MenuSliderItem shapeMix, skinMix;
        private PedHeadBlendData data;
        private bool refreshing;

        internal FaceSelectionMenu(Menu menu)
        {
            this.menu = menu;
            menu.OnListIndexChange += OnListChange;
            menu.OnSliderPositionChange += OnSliderChange;
        }

        private static int MaxFaceId => GetResourceState("cfx_onx_mp_faces") == "started" ? 97 : 45;

        internal void Reset(bool male)
        {
            int id = male ? 0 : 21;
            Apply(new PedHeadBlendData(id, id, id, id, id, id, 0f, 0f, 0f, false));
        }

        internal void Randomize(Random random)
        {
            // Keep addon shapes independent of the vanilla skin IDs.
            Apply(new PedHeadBlendData(random.Next(MaxFaceId + 1), random.Next(MaxFaceId + 1), 0,
                random.Next(46), random.Next(46), 0,
                (float)random.NextDouble(), (float)random.NextDouble(), 0f, false));
        }

        internal void Refresh()
        {
            refreshing = true;
            try
            {
                data = Game.PlayerPed.GetHeadBlendData();
                menu.ClearMenuItems();
                ids.Clear();
                int max = MaxFaceId;
                var faceIds = Enumerable.Range(0, max + 1).ToList();
                var skinIds = Enumerable.Range(0, 46).ToList();
                face = AddList("Face", faceIds, DirectId(data.FirstFaceShape, data.SecondFaceShape,
                    data.ThirdFaceShape, data.ParentFaceShapePercent, data.ParentThirdUnkPercent), true, true,
                    "Pick a complete face with left/right. Skin tone stays the same. ONX faces appear when the ONX face pack is running.");
                skin = AddList("Skin Tone", skinIds, DirectId(data.FirstSkinTone, data.SecondSkinTone,
                    data.ThirdSkinTone, data.ParentSkinTonePercent, data.ParentThirdUnkPercent), true, false,
                    "Pick a skin tone with left/right. Your selected face shape stays the same.");
                faceA = AddList("Blend Face A", faceIds, data.FirstFaceShape, false, true,
                    "Optional: choose the first face for a custom blend.");
                faceB = AddList("Blend Face B", faceIds, data.SecondFaceShape, false, true,
                    "Optional: choose the second face for a custom blend.");
                shapeMix = new MenuSliderItem("Face Blend", "Optional: left is Face A, right is Face B.",
                    0, 10, SliderPosition(data.ParentFaceShapePercent), true);
                menu.AddMenuItem(shapeMix);
                skinA = AddList("Blend Skin A", skinIds, data.FirstSkinTone, false, false,
                    "Optional: choose the first skin tone for a custom blend.");
                skinB = AddList("Blend Skin B", skinIds, data.SecondSkinTone, false, false,
                    "Optional: choose the second skin tone for a custom blend.");
                skinMix = new MenuSliderItem("Skin Blend", "Optional: left is Skin A, right is Skin B.",
                    0, 10, SliderPosition(data.ParentSkinTonePercent), true);
                menu.AddMenuItem(skinMix);
                menu.RefreshIndex();
            }
            finally
            {
                refreshing = false;
            }
        }

        private static int SliderPosition(float value) => Math.Max(0, Math.Min(10, (int)Math.Round(value * 10f)));

        private static int DirectId(int first, int second, int third, float mix, float thirdMix)
        {
            int result = first == second || mix == 0f ? first : mix == 1f ? second : -1;
            return thirdMix == 1f ? third : thirdMix == 0f || third == result ? result : -1;
        }

        private MenuListItem AddList(string title, List<int> available, int selected, bool direct, bool shapes, string description)
        {
            var values = new List<int>(available);
            if (direct)
            {
                values.Insert(0, -1);
            }
            else if (!values.Contains(selected))
            {
                // Retain an unavailable saved ID for display without silently replacing it.
                values.Add(selected);
            }
            var labels = values.Select(id => id == -1 ? "Custom blend (keep current)" :
                !available.Contains(id) ? $"Saved #{id} (unavailable)" :
                shapes && id >= 46 ? $"ONX Face #{id}" : shapes ? $"GTA Face #{id}" : $"Skin #{id}").ToList();
            var item = new MenuListItem(title, labels, Math.Max(0, values.IndexOf(selected)), description);
            ids.Add(item, values);
            menu.AddMenuItem(item);
            return item;
        }

        private void OnListChange(Menu sender, MenuListItem item, int oldIndex, int newIndex, int itemIndex)
        {
            if (refreshing || !ids.TryGetValue(item, out var values) || newIndex < 0 || newIndex >= values.Count)
            {
                return;
            }
            int id = values[newIndex];
            bool shapes = item == face || item == faceA || item == faceB;
            // Leave the keep-current entry selected so users can scroll through it
            // when wrapping from the first face to the last (or vice versa).
            if (id < 0) return;
            if (id > (shapes ? MaxFaceId : 45))
            {
                RefreshSelections();
                return;
            }
            data = Game.PlayerPed.GetHeadBlendData();
            Apply(new PedHeadBlendData(
                item == face || item == faceA ? id : data.FirstFaceShape,
                item == face || item == faceB ? id : data.SecondFaceShape,
                item == face ? id : data.ThirdFaceShape,
                item == skin || item == skinA ? id : data.FirstSkinTone,
                item == skin || item == skinB ? id : data.SecondSkinTone,
                item == skin ? id : data.ThirdSkinTone,
                item == face ? 0f : data.ParentFaceShapePercent,
                item == skin ? 0f : data.ParentSkinTonePercent,
                item == face || item == skin ? data.ParentThirdUnkPercent : 0f,
                data.IsParentInheritance));
            RefreshSelections();
        }

        private void OnSliderChange(Menu sender, MenuSliderItem item, int oldPosition, int newPosition, int itemIndex)
        {
            if (refreshing || (item != shapeMix && item != skinMix)) return;
            data = Game.PlayerPed.GetHeadBlendData();
            float mix = Math.Max(0, Math.Min(10, newPosition)) / 10f;
            Apply(new PedHeadBlendData(data.FirstFaceShape, data.SecondFaceShape, data.ThirdFaceShape,
                data.FirstSkinTone, data.SecondSkinTone, data.ThirdSkinTone,
                item == shapeMix ? mix : data.ParentFaceShapePercent,
                item == skinMix ? mix : data.ParentSkinTonePercent, 0f, data.IsParentInheritance));
            // MenuAPI increments/decrements this slider after the callback returns.
            RefreshSelections(item);
        }

        private void RefreshSelections(MenuSliderItem changingSlider = null)
        {
            refreshing = true;
            try
            {
                Select(face, DirectId(data.FirstFaceShape, data.SecondFaceShape, data.ThirdFaceShape,
                    data.ParentFaceShapePercent, data.ParentThirdUnkPercent));
                Select(skin, DirectId(data.FirstSkinTone, data.SecondSkinTone, data.ThirdSkinTone,
                    data.ParentSkinTonePercent, data.ParentThirdUnkPercent));
                Select(faceA, data.FirstFaceShape);
                Select(faceB, data.SecondFaceShape);
                Select(skinA, data.FirstSkinTone);
                Select(skinB, data.SecondSkinTone);
                if (changingSlider != shapeMix) shapeMix.Position = SliderPosition(data.ParentFaceShapePercent);
                if (changingSlider != skinMix) skinMix.Position = SliderPosition(data.ParentSkinTonePercent);
            }
            finally
            {
                refreshing = false;
            }
        }

        private void Select(MenuListItem item, int id) => item.ListIndex = Math.Max(0, ids[item].IndexOf(id));

        private void Apply(PedHeadBlendData value)
        {
            data = value;
            SetPedHeadBlendData(Game.PlayerPed.Handle, data.FirstFaceShape, data.SecondFaceShape, data.ThirdFaceShape,
                data.FirstSkinTone, data.SecondSkinTone, data.ThirdSkinTone, data.ParentFaceShapePercent,
                data.ParentSkinTonePercent, data.ParentThirdUnkPercent, data.IsParentInheritance);
        }
    }
}
