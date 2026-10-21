// Run: dotnet run --project tests/personal-vehicle-registry
// Drives the production PersonalVehicleRegistry (who may kick passengers out of which vehicle)
// and checks the server wiring: Kick Passengers checks ownership first, registration requires the
// driver seat, and the removed leaky events are gone.
using System;
using System.IO;
using System.Text.RegularExpressions;
using vMenuServer;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    static int Main()
    {
        var r = new PersonalVehicleRegistry();
        Check(!r.Owns("1", 100, 5000, 42), "nobody owns a vehicle before registering");

        var t = r.Begin("1");
        Check(!r.Owns("1", 100, 5000, 42), "registration in progress grants nothing");
        r.Set("1", t, 100, 5000, 42);
        Check(r.Owns("1", 100, 5000, 42), "registered vehicle is owned");
        Check(!r.Owns("2", 100, 5000, 42), "another player does not own it");
        Check(!r.Owns("1", 101, 5000, 42), "a different net id is not owned");
        Check(!r.Owns("1", 100, 5001, 42), "same net id reused by another entity is not owned");
        Check(!r.Owns("1", 100, 5000, 43), "same net id with another model is not owned");

        // Switching vehicles: the old one stops counting at once, and a slower, older
        // registration finishing late cannot overwrite the newer one.
        var older = r.Begin("1");
        var newer = r.Begin("1");
        Check(!r.Owns("1", 100, 5000, 42), "starting a new registration forgets the old vehicle");
        r.Set("1", older, 200, 6000, 7);
        Check(!r.Owns("1", 200, 6000, 7), "a stale registration is ignored");
        r.Set("1", newer, 300, 7000, 8);
        Check(r.Owns("1", 300, 7000, 8), "the newest registration wins");

        // Clearing (net id 0 from the client) is a registration that never completes.
        r.Begin("1");
        Check(!r.Owns("1", 300, 7000, 8), "clearing the personal vehicle revokes it");

        var t2 = r.Begin("2");
        r.Set("2", t2, 400, 8000, 9);
        r.Remove("2");
        Check(!r.Owns("2", 400, 8000, 9), "a dropped player owns nothing");
        Console.WriteLine("PASS: registry (owner only, reused net ids, stale registrations, clear, drop)");

        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "vMenu.sln"))) dir = Path.GetDirectoryName(dir);
        Check(dir != null, "repo root found");
        var server = File.ReadAllText(Path.Combine(dir, "vMenuServer", "MainServer.cs"));
        var kick = Regex.Match(server, @"internal void GetOutOfCar\(.*?\n        }", RegexOptions.Singleline).Value;
        Check(kick.Length > 0, "GetOutOfCar found");
        var owns = kick.IndexOf("personalVehicles.Owns(", StringComparison.Ordinal);
        Check(owns > 0 && owns < kick.IndexOf("TaskLeaveVehicle", StringComparison.Ordinal), "Kick Passengers checks ownership before removing anyone");
        var register = Regex.Match(server, @"internal async void SetPersonalVehicle\(.*?\n        }", RegexOptions.Singleline).Value;
        Check(register.Contains("GetPedInVehicleSeat(vehicle, -1) == ped"), "registration requires the driver seat");
        Check(server.Contains("personalVehicles.Remove(sourcePlayer.Handle);"), "dropped players are forgotten");

        foreach (var removed in new[] { "vMenu:GetPlayerIdentifiers", "vMenu:RequestPlayerList", "vMenu:GetPlayerCoords",
            "vMenu:KickPlayer", "vMenu:KillPlayer", "vMenu:SummonPlayer", "vMenu:SendMessageToPlayer", "BanManager" })
        {
            Check(!server.Contains(removed), "removed from MainServer: " + removed);
        }
        Check(!File.Exists(Path.Combine(dir, "vMenuServer", "BanManager.cs")), "BanManager.cs deleted (ban list leak, bans)");
        var pv = File.ReadAllText(Path.Combine(dir, "vMenu", "menus", "PersonalVehicle.cs"));
        Check(Regex.Matches(pv, @"TriggerServerEvent\(""vMenu:SetPersonalVehicle"",").Count == 2, "client reports setting and clearing the personal vehicle");
        Console.WriteLine("PASS: server and client wiring");
        return 0;
    }
}
