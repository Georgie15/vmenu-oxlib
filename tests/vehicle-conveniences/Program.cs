// Run: dotnet run --project tests/vehicle-conveniences
// Drives the production SavedVehicleTracker (the "save your changes?" offer) and
// the personal vehicle lock shortcuts, then checks how vMenu wires them.
using System;
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
        // --- the update offer -------------------------------------------------------------------
        var tracker = new SavedVehicleTracker();
        var look = "red";
        var looks = 0;
        string Look() { looks++; return look; }
        bool Exists(string name) => name == "Cruiser";

        tracker.BeginVisit(10, Look);
        Check(!tracker.EndVisit(10, Look, Exists, out _, out _) && looks == 0, "an untracked vehicle is never looked at");

        tracker.Track(10, "Cruiser", "Police");
        tracker.BeginVisit(10, Look);
        Check(!tracker.EndVisit(10, Look, Exists, out _, out _), "no offer when nothing changed");

        tracker.BeginVisit(10, Look);
        look = "blue";
        Check(tracker.EndVisit(10, Look, Exists, out var name, out var category) && name == "Cruiser" && category == "Police",
            "a change during the visit offers to update that save, in its category");

        tracker.BeginVisit(10, Look);
        Check(!tracker.EndVisit(10, Look, Exists, out _, out _), "after declining, the same look isn't offered again");

        tracker.BeginVisit(10, Look);
        look = "green";
        tracker.BeginVisit(10, Look); // Vehicle Options reopens on the way back from a submenu
        Check(tracker.EndVisit(10, Look, Exists, out _, out _), "returning from a submenu keeps the visit's starting look");

        tracker.BeginVisit(10, Look);
        look = "white";
        Check(!tracker.EndVisit(11, Look, Exists, out _, out _), "no offer when the player is in another vehicle");
        tracker.BeginVisit(10, Look);
        look = "black";
        Check(!tracker.EndVisit(0, Look, Exists, out _, out _), "no offer on foot");

        tracker.Track(10, "Renamed", "Police");
        tracker.BeginVisit(10, Look);
        look = "gold";
        Check(!tracker.EndVisit(10, Look, Exists, out _, out _), "no offer when the save was renamed or deleted");

        tracker.Track(10, "Cruiser", "Police");
        tracker.BeginVisit(10, Look);
        look = "silver";
        tracker.Track(10, "Cruiser", "Police"); // saved during the visit
        Check(!tracker.EndVisit(10, Look, Exists, out _, out _), "saving during the visit ends it: nothing to offer");
        Console.WriteLine("PASS: update offer only for a tracked, changed, still-saved vehicle the player is driving");

        // --- auto-lock ---------------------------------------------------------------------------
        var autoLock = new PersonalVehicleAutoLock();
        Check(!autoLock.Update(true, 20f, false, false), "not armed until the player has been near the car");
        Check(!autoLock.Update(true, 2f, true, false), "near (or in) the car: armed, nothing to do");
        Check(!autoLock.Update(true, 12f, false, false), "between 10 and 15 m: still waiting");
        Check(autoLock.Update(true, 16f, false, false), "walking past 15 m locks it");
        Check(!autoLock.Update(true, 30f, false, false), "once per walk away");
        autoLock.Update(true, 5f, false, false);
        Check(!autoLock.Update(true, 16f, true, false), "nobody is locked in: a passenger stops it");
        autoLock.Update(true, 5f, false, false);
        Check(!autoLock.Update(true, 16f, false, true), "already locked: nothing to do");
        autoLock.Update(true, 5f, false, false);
        Check(!autoLock.Update(false, 16f, false, false), "option off: never locks");
        Check(!autoLock.Update(true, 16f, false, false), "turning it off disarms it");
        Console.WriteLine("PASS: auto-lock once per walk away, never with someone inside");

        // --- manual lock input ----------------------------------------------------------------------
        var lockInput = new PersonalVehicleLockInput();
        var lastTackleFrame = -1;
        bool Tap(int frame, int time, bool fromHorn = false) =>
            lockInput.RegisterTap(frame, time, fromHorn, tapFrame => lastTackleFrame >= tapFrame);

        Check(!Tap(10, 1000), "one tap does not toggle");
        Check(!Tap(10, 1010, fromHorn: true), "the same key on both shortcuts still counts as one tap");
        Check(Tap(15, 1100), "two quick taps toggle once");
        Check(!Tap(15, 1300, fromHorn: true), "a delayed horn release cannot toggle the same pair again");
        Check(!Tap(16, 1400, fromHorn: true), "the previous pair does not carry over to the next press");
        Check(!Tap(17, 1500), "taps on different shortcuts do not form a pair");
        lastTackleFrame = 18;
        Check(!Tap(18, 1600), "a tackle cannot be the second tap");
        Check(!Tap(19, 1700), "a tackle invalidates the pending first tap");
        Check(!Tap(18, 1750, fromHorn: true), "a late release of a tackle press is still ignored");
        Check(Tap(20, 1800), "two fresh taps work after the tackle");
        Check(!Tap(21, 1900, fromHorn: true), "the horn needs two taps on its own");
        Check(!Tap(35, 2400, fromHorn: true), "a tap after the horn window starts a new pair");
        Check(Tap(36, 2500, fromHorn: true), "the horn shortcut locks on the second quick tap");
        Check(!Tap(37, 2600, fromHorn: true), "arm the horn shortcut again");
        lockInput.ResetHornTap();
        Check(!Tap(38, 2700, fromHorn: true), "leaving range clears the pending horn tap");
        Console.WriteLine("PASS: double-tap locks, tackle cancellation and shared-key deduplication");

        // --- wiring --------------------------------------------------------------------------------
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(dir, Path.Combine(parts))).Replace("\r\n", "\n");
        var common = Read("vMenu", "CommonFunctions.cs");
        var save = Regex.Match(common, @"public static async void SaveVehicle\(.*?\n        \}\n", RegexOptions.Singleline).Value;
        Check(save.Contains("var vi = CaptureVehicleInfo(veh, existingCatergory);") && save.Contains("vi.cadSaveToken = IdentityBridge.VehicleSaveToken(veh.Handle);"),
            "saving uses the same capture as the change check, then adds the CAD token");
        Check(Regex.Matches(save, @"SavedVehicleChanges\.Track\(veh\.Handle").Count == 2, "a new save and an update both track the vehicle");
        Check(Regex.IsMatch(common, @"await BaseScript\.Delay\(650\);\s*SavedVehicleChanges\.Track\(vehicle\.Handle, saveName, vehicleInfo\.Category\);"),
            "a saved vehicle is tracked once its setup is applied");
        Check(common.Contains("VehicleLook(Vehicle veh) => JsonConvert.SerializeObject(CaptureVehicleInfo(veh, null));"), "the look is the captured setup");
        Check(Regex.IsMatch(Read("vMenu", "MainMenu.cs"), @"if \(!isMenuOpen\)\s*\{\s*OfferToUpdateSavedVehicle\(\);"), "the offer comes when every menu closes");
        var options = Read("vMenu", "menus", "VehicleOptions.cs");
        Check(options.Contains("SavedVehicleChanges.BeginVisit("), "the visit starts when Vehicle Options opens");

        Check(common.Contains("public static bool IsVehicleLocked(Vehicle veh) => GetVehicleDoorLockStatus(veh.Handle) >= 2;"), "lock state is read from the vehicle");
        foreach (var file in new[] { "FunctionsController.cs", Path.Combine("menus", "PersonalVehicle.cs") })
        {
            Check(!Read("vMenu", file).Contains("GetVehicleDoorsLockedForPlayer"), file + " no longer passes a ped handle as a player id");
        }
        var fc = Read("vMenu", "FunctionsController.cs");
        Check(Regex.IsMatch(fc, @"if \(IsAllowed\(Permission\.PVLockDoors\)\)\s*\{\s*Tick \+= PersonalVehicleOptions;\s*Tick \+= PersonalVehicleAutoLockTick;"),
            "auto-lock runs only with the lock permission");
        Check(Read("vMenu", "menus", "PersonalVehicle.cs").Contains("SetResourceKvpInt(\"vmenu_pv_autolock\", _checked ? 1 : 0);"), "the setting is remembered");

        foreach (var (permission, binding) in new[] { ("VOEngine", "VehEngine"), ("VODoors", "VehHood"), ("VODoors", "VehTrunk"), ("VOWindows", "VehWindows"), ("VOCycleSeats", "VehSeat") })
        {
            Check(Regex.IsMatch(options, @"if \(IsAllowed\(Permission\." + permission + @"\)\)\s*\{[^}]*Bind\(""" + binding + "\""), binding + " needs " + permission);
        }
        Check(options.Contains("RegisterKeyMapping(command, description, \"keyboard\", \"\");"), "vehicle key bindings are unbound by default");
        Console.WriteLine("PASS: update offer, lock check, auto-lock and vehicle key binding wiring");
        return 0;
    }
}
