using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace vMenuClient
{
    /// <summary>
    /// MP Ped Customization > Department Outfits: uniforms a department's heads publish, which its
    /// members on duty wear with one click. The server (build/vMenu/server/deptOutfits.lua) keeps them
    /// in the vmenu_dept_outfits table and decides every request: who is on duty with which org
    /// (PoliceEMSActivity) and who manages which org (vMenu.DeptOutfits.Manage.&lt;org&gt; and
    /// vMenu.DeptOutfits.ManageAll aces). A preset is a <see cref="SavedOutfit"/> (the Saved Outfits
    /// format, with collection references) for one freemode model, so it is put on exactly like a
    /// saved outfit. This file reads the server's replies; no game calls, so it can be tested.
    /// </summary>
    internal static class DepartmentOutfits
    {
        internal const string Convar = "vmenu_dept_outfits";
        internal const string RequestEvent = "vMenu:DeptOutfits:Request";
        internal const string ReplyEvent = "vMenu:DeptOutfits:Reply";
        internal const int MaxNameLength = 40;

        /// <summary>A preset name typed by a manager, trimmed. The server checks it again.</summary>
        internal static string CleanName(string input, out string error)
        {
            error = null;
            var name = string.Join(" ", (input ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (name.Length == 0)
            {
                error = "Enter a name.";
                return null;
            }
            if (name.Length > MaxNameLength)
            {
                error = $"Names can be up to {MaxNameLength} characters.";
                return null;
            }
            if (name.Any(c => !(char.IsLetterOrDigit(c) && c < 128) && " -_'.()&/#+,".IndexOf(c) < 0))
            {
                error = "Names can use letters, numbers, spaces and - _ ' . ( ) & / # + ,";
                return null;
            }
            return name;
        }

        /// <summary>
        /// Slots of an outfit the game couldn't place in a collection. A preset is shared with a whole
        /// department and must survive clothing pack changes, so it is only published when every
        /// clothing and prop slot has a reference.
        /// </summary>
        internal static List<ClothingSlot> MissingRefs(SavedOutfit outfit) =>
            ClothingResolver.OutfitSlots.Where(slot => outfit?.Refs?.Get(slot) == null || outfit.Refs.Get(slot).IsPending).ToList();
    }

    /// <summary>One reply from the server: <c>{ ok, message, ... }</c>.</summary>
    internal sealed class DepartmentOutfitReply
    {
        internal bool Ok;
        internal string Message;
        internal JObject Data = new();

        internal static DepartmentOutfitReply Failed(string message) => new() { Ok = false, Message = message };

        internal static DepartmentOutfitReply Parse(string json)
        {
            JObject data = null;
            try
            {
                data = string.IsNullOrWhiteSpace(json) ? null : JToken.Parse(json) as JObject;
            }
            catch (JsonException)
            {
            }
            if (data == null)
            {
                return Failed("The server's answer couldn't be read.");
            }
            return new DepartmentOutfitReply
            {
                Ok = data["ok"]?.Type == JTokenType.Boolean && (bool)data["ok"],
                Message = data["message"]?.Type == JTokenType.String ? (string)data["message"] : null,
                Data = data,
            };
        }

        internal string Text(string field) => Data[field]?.Type == JTokenType.String ? (string)Data[field] : null;
    }

    internal sealed class DepartmentOrg
    {
        internal string Id;
        internal string Label;
    }

    internal sealed class DepartmentOutfitEntry
    {
        internal int Id;
        internal string Org;
        internal string Name;
        internal string Updated;
        internal string By;
        internal bool CanManage;
    }

    /// <summary>
    /// The list reply: the org you're on duty with (if any), the orgs you manage, and the presets of
    /// both for your body type.
    /// </summary>
    internal sealed class DepartmentOutfitListing
    {
        internal DepartmentOrg Duty;
        internal bool DutyAvailable;
        internal readonly List<DepartmentOrg> Manage = new();
        internal readonly List<DepartmentOutfitEntry> Outfits = new();

        // Lua's json writes an empty table as [] or {}: both are an empty list here.
        private static IEnumerable<JObject> Items(JToken token) => token is JArray list ? list.OfType<JObject>() : Enumerable.Empty<JObject>();

        private static string Text(JToken token) => token?.Type == JTokenType.String ? (string)token : null;

        private static DepartmentOrg Org(JToken token)
        {
            var id = Text(token?["id"]);
            return string.IsNullOrEmpty(id) ? null : new DepartmentOrg { Id = id, Label = Text(token["label"]) ?? id };
        }

        internal static DepartmentOutfitListing From(DepartmentOutfitReply reply)
        {
            var data = reply?.Data ?? new JObject();
            var listing = new DepartmentOutfitListing
            {
                Duty = data["duty"] is JObject duty ? Org(duty) : null,
                DutyAvailable = data["dutyAvailable"]?.Type == JTokenType.Boolean && (bool)data["dutyAvailable"],
            };
            foreach (var org in Items(data["manage"]).Select(Org).Where(o => o != null))
            {
                if (listing.Manage.All(m => m.Id != org.Id))
                {
                    listing.Manage.Add(org);
                }
            }
            foreach (var item in Items(data["outfits"]))
            {
                var id = item["id"];
                var name = Text(item["name"]);
                var org = Text(item["org"]);
                if (id == null || (id.Type != JTokenType.Integer && id.Type != JTokenType.Float) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(org))
                {
                    continue;
                }
                listing.Outfits.Add(new DepartmentOutfitEntry
                {
                    Id = (int)id.Value<double>(),
                    Org = org,
                    Name = name,
                    Updated = Text(item["updated"]),
                    By = Text(item["by"]),
                    CanManage = item["manage"]?.Type == JTokenType.Boolean && (bool)item["manage"],
                });
            }
            return listing;
        }

        internal bool Manages(string org) => Manage.Any(m => m.Id == org);

        internal string LabelOf(string org) =>
            Duty?.Id == org ? Duty.Label : Manage.FirstOrDefault(m => m.Id == org)?.Label ?? org;

        /// <summary>The orgs to show, in order: the one you're on duty with, then those you manage.</summary>
        internal List<DepartmentOrg> Sections()
        {
            var sections = new List<DepartmentOrg>();
            if (Duty != null)
            {
                sections.Add(Duty);
            }
            sections.AddRange(Manage.Where(m => m.Id != Duty?.Id));
            return sections;
        }

        internal List<DepartmentOutfitEntry> OutfitsOf(string org) =>
            Outfits.Where(o => o.Org == org).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
