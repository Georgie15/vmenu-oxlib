using CitizenFX.Core;
using CitizenFX.Core.Native;
using vMenuClient.menus;

var menu = new Recording().GetMenu();

void Expect(params string[] expected)
{
    if (!API.Calls.SequenceEqual(expected))
        throw new Exception($"Expected {string.Join(", ", expected)}. Got {string.Join(", ", API.Calls)}.");
    API.Calls.Clear();
}

menu.Select("Take Photo");
Expect("begin");
menu.Select("Take Photo");
Expect("alert: A photo is already in progress.");
BaseScript.NextFrame();
Expect("capture status");
API.CaptureStatus = 0;
BaseScript.NextFrame();
Expect("capture status", "save");
BaseScript.NextFrame();
Expect("save status");
API.SaveStatus = 0;
BaseScript.NextFrame();
Expect("save status", "info: Photo saved to the Pause Menu gallery.", "free");

API.BeginAccepted = false;
menu.Select("Take Photo");
Expect("begin", "alert: Could not start taking a photo.");

API.BeginAccepted = true;
API.CaptureStatus = 2;
menu.Select("Take Photo");
Expect("begin");
BaseScript.NextFrame();
Expect("capture status", "alert: Could not capture the photo.", "free");

API.CaptureStatus = 0;
API.SaveAccepted = false;
menu.Select("Take Photo");
Expect("begin");
BaseScript.NextFrame();
Expect("capture status", "save", "alert: Could not save the photo to the Pause Menu gallery.", "free");

API.SaveAccepted = true;
API.SaveStatus = 2;
menu.Select("Take Photo");
Expect("begin");
BaseScript.NextFrame();
Expect("capture status", "save");
BaseScript.NextFrame();
Expect("save status", "alert: Could not save the photo to the Pause Menu gallery.", "free");
Console.WriteLine("PASS: Take Photo waits for capture and save, rejects duplicate requests, and cleans up after failures");

namespace CitizenFX.Core
{
    static class BaseScript
    {
        private static TaskCompletionSource nextFrame = new();

        public static Task Delay(int milliseconds) => nextFrame.Task;

        public static void NextFrame()
        {
            var frame = nextFrame;
            nextFrame = new();
            frame.SetResult();
        }
    }
}

namespace CitizenFX.Core.Native
{
    static class API
    {
        public static readonly List<string> Calls = new();
        public static bool BeginAccepted = true;
        public static bool SaveAccepted = true;
        public static int CaptureStatus = 1;
        public static int SaveStatus = 1;

        public static bool BeginTakeHighQualityPhoto() { Calls.Add("begin"); return BeginAccepted; }
        public static int GetStatusOfTakeHighQualityPhoto() { Calls.Add("capture status"); return CaptureStatus; }
        public static bool SaveHighQualityPhoto(int unused) { Calls.Add("save"); return SaveAccepted; }
        public static int GetStatusOfSaveHighQualityPhoto() { Calls.Add("save status"); return SaveStatus; }
        public static void FreeMemoryForHighQualityPhoto() => Calls.Add("free");

        public static void AddTextEntryByHash(uint hash, string value) { }
        public static void AddTextEntry(string label, string value) { }
        public static bool IsRecording() => false;
        public static void StartRecording(int mode) { }
        public static int GetHashKey(string value) => 0;
        public static void ActivateFrontendMenu(uint hash, bool togglePause, int component) { }
        public static void StopRecordingAndSaveClip() { }
        public static void ActivateRockstarEditor() { }
        public static bool IsPauseMenuActive() => false;
        public static void DoScreenFadeIn(int duration) { }
    }
}

namespace MenuAPI
{
    public class MenuItem(string text, string description)
    {
        public string Text { get; } = text;
        public string Description { get; } = description;
    }

    public class Menu
    {
        private readonly List<MenuItem> items = new();
        public event Action<Menu, MenuItem, int> OnItemSelect;
        public Menu(string title, string subtitle) { }
        public void AddMenuItem(MenuItem item) => items.Add(item);

        public void Select(string text)
        {
            var index = items.FindIndex(item => item.Text == text);
            OnItemSelect(this, items[index], index);
        }
    }
}

namespace vMenuClient
{
    static class CommonFunctions
    {
        public static Task<bool> GetUserConfirmation(string title, string message) => Task.FromResult(false);
        public static void QuitSession() { }
    }

    static class Notify
    {
        public static void Alert(string message, bool blink = true, bool showInBrief = true) => API.Calls.Add("alert: " + message);
        public static void Info(string message) => API.Calls.Add("info: " + message);
    }
}

namespace vMenuShared
{
    static class ConfigManager
    {
        public enum Setting { vmenu_quit_session_in_rockstar_editor }
        public static bool GetSettingsBool(Setting setting) => false;
    }
}
