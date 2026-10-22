using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using MenuAPI;

using vMenuClient.data;

using static CitizenFX.Core.Native.API;
using static vMenuClient.CommonFunctions;
using static vMenuShared.PermissionsManager;

namespace vMenuClient.menus
{
    public class VehicleSpawner
    {
        // Variables
        private Menu menu;
        public static Dictionary<string, uint> AddonVehicles;

        public bool SpawnInVehicle { get; private set; } = UserDefaults.VehicleSpawnerSpawnInside;
        public bool ReplaceVehicle { get; private set; } = UserDefaults.VehicleSpawnerReplacePrevious;
        public static List<bool> allowedCategories;

        private void CreateMenu()
        {
            #region initial setup.
            // Create the menu.
            menu = new Menu(" ", "Vehicle Spawner");

            // Create the buttons and checkboxes.
            var spawnByName = new MenuItem("Spawn Vehicle By Model Name", "Enter the name of a vehicle to spawn.");
            var spawnInVeh = new MenuCheckboxItem("Spawn Inside Vehicle", "This will teleport you into the vehicle when you spawn it.", SpawnInVehicle);
            var replacePrev = new MenuCheckboxItem("Replace Previous Vehicle", "This will automatically delete your previously spawned vehicle when you spawn a new vehicle.", ReplaceVehicle);
            var customVehiclesBtn = new MenuItem("Custom Vehicles", "Open the custom vehicle spawner menu.")
            {
                LeftIcon = MenuItem.Icon.CAR
            };
            var enabledCategoryButtons = new Dictionary<MenuItem, string>();

            // Add the items to the menu.
            if (IsAllowed(Permission.VSSpawnByName))
            {
                menu.AddMenuItem(spawnByName);
            }
            menu.AddMenuItem(spawnInVeh);
            menu.AddMenuItem(replacePrev);
            #endregion

            #region favourite and recent vehicles
            var favouritesMenu = new Menu("Vehicle Spawner", "Favourite Vehicles");
            var recentMenu = new Menu("Vehicle Spawner", "Recent Vehicles");
            var favouritesBtn = new MenuItem("Favourite Vehicles", "Vehicles you've starred. Add the one you're in, or one from Recent Vehicles.") { Label = "→→→" };
            var recentBtn = new MenuItem("Recent Vehicles", $"The last {ModelShortlist.MaxRecent} vehicles you spawned from the spawner, by name or from your favourites.") { Label = "→→→" };
            MenuController.AddSubmenu(menu, favouritesMenu);
            MenuController.AddSubmenu(menu, recentMenu);
            menu.AddMenuItem(favouritesBtn);
            menu.AddMenuItem(recentBtn);
            MenuController.BindMenuItem(menu, favouritesMenu, favouritesBtn);
            MenuController.BindMenuItem(menu, recentMenu, recentBtn);
            CreateShortlistMenus(favouritesMenu, recentMenu);
            #endregion

            #region custom vehicles button
            menu.AddMenuItem(customVehiclesBtn);
            #endregion

            // These are the max speed, acceleration, braking and traction values per vehicle class.
            var speedValues = new float[23]
            {
                44.9374657f,
                50.0000038f,
                48.862133f,
                48.1321335f,
                50.7077942f,
                51.3333359f,
                52.3922348f,
                53.86687f,
                52.03867f,
                49.2241631f,
                39.6176529f,
                37.5559425f,
                42.72843f,
                21.0f,
                45.0f,
                65.1952744f,
                109.764259f,
                42.72843f,
                56.5962219f,
                57.5398865f,
                43.3140678f,
                26.66667f,
                53.0537224f
            };
            var accelerationValues = new float[23]
            {
                0.34f,
                0.29f,
                0.335f,
                0.28f,
                0.395f,
                0.39f,
                0.66f,
                0.42f,
                0.425f,
                0.475f,
                0.21f,
                0.3f,
                0.32f,
                0.17f,
                18.0f,
                5.88f,
                21.0700016f,
                0.33f,
                14.0f,
                6.86f,
                0.32f,
                0.2f,
                0.76f
            };
            var brakingValues = new float[23]
            {
                0.72f,
                0.95f,
                0.85f,
                0.9f,
                1.0f,
                1.0f,
                1.3f,
                1.25f,
                1.52f,
                1.1f,
                0.6f,
                0.7f,
                0.8f,
                3.0f,
                0.4f,
                3.5920403f,
                20.58f,
                0.9f,
                2.93960738f,
                3.9472363f,
                0.85f,
                5.0f,
                1.3f
            };
            var tractionValues = new float[23]
            {
                2.3f,
                2.55f,
                2.3f,
                2.6f,
                2.625f,
                2.65f,
                2.8f,
                2.782f,
                2.9f,
                2.95f,
                2.0f,
                3.3f,
                2.175f,
                2.05f,
                0.0f,
                1.6f,
                2.15f,
                2.55f,
                2.57f,
                3.7f,
                2.05f,
                2.5f,
                3.2925f
            };

            #region vehicle classes submenus
            // Loop through all the vehicle classes.
            for (var vehClass = 0; vehClass < 23; vehClass++)
            {
                // Get the class name.
                var className = GetLabelText($"VEH_CLASS_{vehClass}");

                // Create a button & a menu for it, add the menu to the menu pool and add & bind the button to the menu.
                var btn = new MenuItem(className, $"Spawn a vehicle from the ~o~{className} ~s~class.")
                {
                    Label = "→→→"
                };

                var vehicleClassMenu = new Menu("Vehicle Spawner", className);

                MenuController.AddSubmenu(menu, vehicleClassMenu);
                menu.AddMenuItem(btn);

                if (allowedCategories[vehClass])
                {
                    MenuController.BindMenuItem(menu, vehicleClassMenu, btn);
                    enabledCategoryButtons.Add(btn, btn.Description);
                }
                else
                {
                    btn.LeftIcon = MenuItem.Icon.LOCK;
                    btn.Description = "This category has been disabled by the server owner.";
                    btn.Enabled = false;
                }

                // The vehicles of a class (label and stat natives for every model) are added the first
                // time it is opened. Building all ~800 entries when the menu was created was a hitch.
                var classIndex = vehClass;
                var populated = false;
                void PopulateClass()
                {
                    if (populated)
                    {
                        return;
                    }
                    populated = true;

                    // Create a dictionary for the duplicate vehicle names (in this vehicle class).
                    var duplicateVehNames = new Dictionary<string, int>();

                    #region Add vehicles per class
                    // Loop through all the vehicles in the vehicle class.
                    foreach (var veh in VehicleData.Vehicles.VehicleClasses[className])
                    {
                        // Convert the model name to start with a Capital letter, converting the other characters to lowercase. 
                        var properCasedModelName = veh[0].ToString().ToUpper() + veh.ToLower().Substring(1);

                        // Get the localized vehicle name, if it's "NULL" (no label found) then use the "properCasedModelName" created above.
                        var vehName = GetVehDisplayNameFromModel(veh) != "NULL" ? GetVehDisplayNameFromModel(veh) : properCasedModelName;
                        var vehModelName = veh;
                        var model = (uint)GetHashKey(vehModelName);

                        var topSpeed = Map(GetVehicleModelEstimatedMaxSpeed(model), 0f, speedValues[classIndex], 0f, 1f);
                        var acceleration = Map(GetVehicleModelAcceleration(model), 0f, accelerationValues[classIndex], 0f, 1f);
                        var maxBraking = Map(GetVehicleModelMaxBraking(model), 0f, brakingValues[classIndex], 0f, 1f);
                        var maxTraction = Map(GetVehicleModelMaxTraction(model), 0f, tractionValues[classIndex], 0f, 1f);

                        // Loop through all the menu items and check each item's title/text and see if it matches the current vehicle (display) name.
                        var duplicate = false;
                        for (var itemIndex = 0; itemIndex < vehicleClassMenu.Size; itemIndex++)
                        {
                            // If it matches...
                            if (vehicleClassMenu.GetMenuItems()[itemIndex].Text.ToString() == vehName)
                            {

                                // Check if the model was marked as duplicate before.
                                if (duplicateVehNames.Keys.Contains(vehName))
                                {
                                    // If so, add 1 to the duplicate counter for this model name.
                                    duplicateVehNames[vehName]++;
                                }

                                // If this is the first duplicate, then set it to 2.
                                else
                                {
                                    duplicateVehNames[vehName] = 2;
                                }

                                // The model name is a duplicate, so get the modelname and add the duplicate amount for this model name to the end of the vehicle name.
                                vehName += $" ({duplicateVehNames[vehName]})";

                                // Then create and add a new button for this vehicle.

                                if (DoesModelExist(veh))
                                {
                                    var vehBtn = new MenuItem(vehName)
                                    {
                                        Enabled = true,
                                        Label = $"({vehModelName.ToLower()})",
                                        ItemData = new float[4] { topSpeed, acceleration, maxBraking, maxTraction }
                                    };
                                    vehicleClassMenu.AddMenuItem(vehBtn);
                                }
                                else
                                {
                                    var vehBtn = new MenuItem(vehName, "This vehicle is not available because the model could not be found in your game files. If this is a DLC vehicle, make sure the server is streaming it.")
                                    {
                                        Enabled = false,
                                        Label = $"({vehModelName.ToLower()})",
                                        ItemData = new float[4] { 0f, 0f, 0f, 0f }
                                    };
                                    vehicleClassMenu.AddMenuItem(vehBtn);
                                    vehBtn.RightIcon = MenuItem.Icon.LOCK;
                                }

                                // Mark duplicate as true and break from the loop because we already found the duplicate.
                                duplicate = true;
                                break;
                            }
                        }

                        // If it's not a duplicate, add the model name.
                        if (!duplicate)
                        {
                            if (DoesModelExist(veh))
                            {
                                var vehBtn = new MenuItem(vehName)
                                {
                                    Enabled = true,
                                    Label = $"({vehModelName.ToLower()})",
                                    ItemData = new float[4] { topSpeed, acceleration, maxBraking, maxTraction }
                                };
                                vehicleClassMenu.AddMenuItem(vehBtn);
                            }
                            else
                            {
                                var vehBtn = new MenuItem(vehName, "This vehicle is not available because the model could not be found in your game files. If this is a DLC vehicle, make sure the server is streaming it.")
                                {
                                    Enabled = false,
                                    Label = $"({vehModelName.ToLower()})",
                                    ItemData = new float[4] { 0f, 0f, 0f, 0f }
                                };
                                vehicleClassMenu.AddMenuItem(vehBtn);
                                vehBtn.RightIcon = MenuItem.Icon.LOCK;
                            }
                        }
                    }
                    #endregion

                    vehicleClassMenu.RefreshIndex();
                }
                menu.OnItemSelect += (sender, item, index) =>
                {
                    if (item == btn)
                    {
                        PopulateClass();
                    }
                };

                vehicleClassMenu.ShowVehicleStatsPanel = true;

                // Handle button presses
                vehicleClassMenu.OnItemSelect += async (sender2, item2, index2) =>
                {
                    await SpawnVehicle(VehicleData.Vehicles.VehicleClasses[className][index2], SpawnInVehicle, ReplaceVehicle);
                };

                static void HandleStatsPanel(Menu openedMenu, MenuItem currentItem)
                {
                    if (currentItem != null)
                    {
                        if (currentItem.ItemData is float[] data)
                        {
                            openedMenu.ShowVehicleStatsPanel = true;
                            openedMenu.SetVehicleStats(data[0], data[1], data[2], data[3]);
                            openedMenu.SetVehicleUpgradeStats(0f, 0f, 0f, 0f);
                        }
                        else
                        {
                            openedMenu.ShowVehicleStatsPanel = false;
                        }
                    }
                }

                vehicleClassMenu.OnMenuOpen += (m) =>
                {
                    HandleStatsPanel(m, m.GetCurrentMenuItem());
                };

                vehicleClassMenu.OnIndexChange += (m, oldItem, newItem, oldIndex, newIndex) =>
                {
                    HandleStatsPanel(m, newItem);
                };
            }
            #endregion

            #region handle events
            // Handle button presses.
            menu.OnItemSelect += async (sender, item, index) =>
            {
                if (item == customVehiclesBtn)
                {
                    MenuController.CloseAllMenus();
                    ExecuteCommand("openspawner");
                }
                else if (item == spawnByName)
                {
                    // Passing "custom" as the vehicle name, will ask the user for input.
                    await SpawnVehicle("custom", SpawnInVehicle, ReplaceVehicle);
                }
            };

            // Handle checkbox changes.
            menu.OnCheckboxChange += (sender, item, index, _checked) =>
            {
                if (item == spawnInVeh)
                {
                    SpawnInVehicle = _checked;
                }
                else if (item == replacePrev)
                {
                    ReplaceVehicle = _checked;
                }
            };
            #endregion
        }

        private static ModelShortlist LoadShortlist(string key, int capacity) => ModelShortlist.Parse(GetResourceKvpString(key), capacity);

        private static ModelShortlist LoadFavourites() => LoadShortlist(ModelShortlist.VehicleFavouritesKey, ModelShortlist.MaxFavourites);

        /// <summary>
        /// Records a fresh spawn (spawner, by name, favourites) in Recent Vehicles. Saved vehicles
        /// have their own menu and aren't recorded.
        /// </summary>
        internal static void RememberRecent(uint model)
        {
            var recent = LoadShortlist(ModelShortlist.VehicleRecentKey, ModelShortlist.MaxRecent);
            recent.Touch(model);
            SetResourceKvp(ModelShortlist.VehicleRecentKey, recent.ToJson());
        }

        private static string GetVehicleName(uint model)
        {
            var displayName = GetDisplayNameFromVehicleModel(model);
            var label = GetLabelText(displayName);
            if (!string.IsNullOrEmpty(label) && label != "NULL")
            {
                return label;
            }
            return string.IsNullOrEmpty(displayName) || displayName == "CARNOTFOUND" ? $"Unknown vehicle ({model})" : displayName;
        }

        private Task<int> SpawnFromShortlist(uint model)
            => SpawnVehicle(vehicleHash: model, spawnInside: SpawnInVehicle, replacePrevious: ReplaceVehicle, skipLoad: false, vehicleInfo: new VehicleInfo(), saveName: null);

        /// <summary>
        /// Favourite and Recent Vehicles. Each vehicle is a list item: Spawn, or Remove / (Un)favourite.
        /// Both menus are rebuilt from storage when opened; spawning goes through the normal spawn checks.
        /// </summary>
        private void CreateShortlistMenus(Menu favouritesMenu, Menu recentMenu)
        {
            var addCurrent = new MenuItem("Add My Current Vehicle", "Adds the vehicle you're in to your favourites.");
            var clearRecent = new MenuItem("~r~Clear Recent Vehicles", "Empties this list.");

            MenuListItem VehicleItem(uint model, string secondAction)
                => new(GetVehicleName(model), new List<string> { "Spawn", secondAction }, 0, $"Choose Spawn or {secondAction}, then press Enter.") { ItemData = model };

            void RebuildFavourites()
            {
                favouritesMenu.ClearMenuItems();
                favouritesMenu.AddMenuItem(addCurrent);
                var favourites = LoadFavourites();
                if (favourites.Models.Count == 0)
                {
                    favouritesMenu.AddMenuItem(new MenuItem("No favourites yet", "Add the vehicle you're in, or favourite one from Recent Vehicles.") { Enabled = false });
                }
                foreach (var model in favourites.Models)
                {
                    favouritesMenu.AddMenuItem(VehicleItem(model, "Remove"));
                }
                favouritesMenu.RefreshIndex();
            }

            void RebuildRecent()
            {
                recentMenu.ClearMenuItems();
                var recent = LoadShortlist(ModelShortlist.VehicleRecentKey, ModelShortlist.MaxRecent);
                var favourites = LoadFavourites();
                if (recent.Models.Count == 0)
                {
                    recentMenu.AddMenuItem(new MenuItem("No recent vehicles yet", "Vehicles you spawn from the spawner or by name show up here.") { Enabled = false });
                }
                foreach (var model in recent.Models)
                {
                    recentMenu.AddMenuItem(VehicleItem(model, favourites.Contains(model) ? "Unfavourite" : "Favourite"));
                }
                if (recent.Models.Count > 0)
                {
                    recentMenu.AddMenuItem(clearRecent);
                }
                recentMenu.RefreshIndex();
            }

            // Adds or removes a favourite; false (with a message) if the list is full.
            bool SetFavourite(uint model, bool favourite)
            {
                var favourites = LoadFavourites();
                if (favourite && !favourites.Contains(model) && !favourites.Add(model))
                {
                    Notify.Error($"You can have up to {ModelShortlist.MaxFavourites} favourite vehicles.");
                    return false;
                }
                if (!favourite)
                {
                    favourites.Remove(model);
                }
                SetResourceKvp(ModelShortlist.VehicleFavouritesKey, favourites.ToJson());
                return true;
            }

            favouritesMenu.OnMenuOpen += (m) => RebuildFavourites();
            recentMenu.OnMenuOpen += (m) => RebuildRecent();

            favouritesMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item != addCurrent)
                {
                    return;
                }
                var veh = GetVehicle();
                if (veh == null || !veh.Exists())
                {
                    Notify.Error(CommonErrors.NoVehicle);
                    return;
                }
                var model = (uint)veh.Model.Hash;
                if (LoadFavourites().Contains(model))
                {
                    Notify.Info($"{GetVehicleName(model)} is already a favourite.");
                }
                else if (SetFavourite(model, true))
                {
                    Notify.Success($"{GetVehicleName(model)} added to your favourites.");
                    RebuildFavourites();
                }
            };

            favouritesMenu.OnListItemSelect += async (sender, item, listIndex, itemIndex) =>
            {
                if (item.ItemData is not uint model)
                {
                    return;
                }
                if (listIndex == 0)
                {
                    await SpawnFromShortlist(model);
                }
                else if (SetFavourite(model, false))
                {
                    RebuildFavourites();
                }
            };

            recentMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == clearRecent)
                {
                    DeleteResourceKvp(ModelShortlist.VehicleRecentKey);
                    RebuildRecent();
                }
            };

            recentMenu.OnListItemSelect += async (sender, item, listIndex, itemIndex) =>
            {
                if (item.ItemData is not uint model)
                {
                    return;
                }
                if (listIndex == 0)
                {
                    await SpawnFromShortlist(model);
                    return;
                }
                var favourite = !LoadFavourites().Contains(model);
                if (SetFavourite(model, favourite))
                {
                    Notify.Success(favourite ? $"{GetVehicleName(model)} added to your favourites." : $"{GetVehicleName(model)} removed from your favourites.");
                    RebuildRecent();
                }
            };
        }

        /// <summary>
        /// Create the menu if it doesn't exist, and then returns it.
        /// </summary>
        /// <returns>The Menu</returns>
        public Menu GetMenu()
        {
            if (menu == null)
            {
                CreateMenu();
            }
            return menu;
        }
    }
}
