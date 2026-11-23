using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using CitizenFX.Core;

using MenuAPI;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using vMenuClient.data;

using static CitizenFX.Core.Native.API;
using static vMenuClient.CommonFunctions;
using static vMenuClient.MpPedDataManager;
using static vMenuShared.ConfigManager;

namespace vMenuClient.menus
{
    public class MpPedCustomization
    {
        // Variables
        private Menu menu;
        public Menu createCharacterMenu = new("Create Character", "Create A New Character");
        public Menu savedCharactersMenu = new("vMenu", "Manage Saved Characters");
        public Menu savedCharactersCategoryMenu = new("Category", "I get updated at runtime!");
        public Menu inheritanceMenu = new("vMenu", "Face & Skin");
        private FaceSelectionMenu faceSelection;
        public Menu appearanceMenu = new("vMenu", "Character Appearance Options");
        public Menu faceShapeMenu = new("vMenu", "Character Face Shape Options");
        public Menu tattoosMenu = new("vMenu", "Character Tattoo Options");
        public Menu clothesMenu = new("vMenu", "Character Clothing Options");
        public Menu propsMenu = new("vMenu", "Character Props Options");
        private readonly Menu manageSavedCharacterMenu = new("vMenu", "Manage MP Character");

        // Need to be able to disable/enable these buttons from another class.
        internal MenuItem createMaleBtn = new("Create Male Character", "Create a new male character.") { Label = "→→→" };
        internal MenuItem createFemaleBtn = new("Create Female Character", "Create a new female character.") { Label = "→→→" };
        internal MenuItem editPedBtn = new("Edit Saved Character", "This allows you to edit everything about your saved character. The changes will be saved to this character's save file entry once you hit the save button.");

        // Need to be editable from other functions
        private readonly MenuListItem setCategoryBtn = new("Set Character Category", new List<string> { }, 0, "Sets this character's category. Select to save.");
        private readonly MenuListItem categoryBtn = new("Character Category", new List<string> { }, 0, "Sets this character's category.");

        public static bool DontCloseMenus { get { return MenuController.PreventExitingMenu; } set { MenuController.PreventExitingMenu = value; } }
        public static bool DisableBackButton { get { return MenuController.DisableBackButton; } set { MenuController.DisableBackButton = value; } }
        string selectedSavedCharacterManageName = "";
        private bool isEdidtingPed = false;
        private readonly List<string> facial_expressions = new() { "mood_Normal_1", "mood_Happy_1", "mood_Angry_1", "mood_Aiming_1", "mood_Injured_1", "mood_stressed_1", "mood_smug_1", "mood_sulk_1", };

        public static bool OutfitCodesEnabled = GetSettingsBool(Setting.vmenu_outfitcodes);
        private const string LastMpCharacterSnapshotKey = "vmenu_last_mp_character_snapshot";

        private readonly List<float> faceFeaturesValuesList =
        [
            -1.0f,    // 0
            -0.9f,    // 1
            -0.8f,    // 2
            -0.7f,    // 3
            -0.6f,    // 4
            -0.5f,    // 5
            -0.4f,    // 6
            -0.3f,    // 7
            -0.2f,    // 8
            -0.1f,    // 9
            0.0f,    // 10
            0.1f,    // 11
            0.2f,    // 12
            0.3f,    // 13
            0.4f,    // 14
            0.5f,    // 15
            0.6f,    // 16
            0.7f,    // 17
            0.8f,    // 18
            0.9f,    // 19
            1.0f     // 20
        ];
        private readonly Dictionary<int, KeyValuePair<string, string>> hairOverlays = new Dictionary<int, KeyValuePair<string, string>>()
        {
            { 0, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_a") },
            { 1, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 2, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 3, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_003_a") },
            { 4, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 5, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 6, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 7, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 8, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_008_a") },
            { 9, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 10, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 11, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 12, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 13, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 14, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_long_a") },
            { 15, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_long_a") },
            { 16, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_z") },
            { 17, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_a") },
            { 18, new KeyValuePair<string, string>("mpbusiness_overlays", "FM_Bus_M_Hair_000_a") },
            { 19, new KeyValuePair<string, string>("mpbusiness_overlays", "FM_Bus_M_Hair_001_a") },
            { 20, new KeyValuePair<string, string>("mphipster_overlays", "FM_Hip_M_Hair_000_a") },
            { 21, new KeyValuePair<string, string>("mphipster_overlays", "FM_Hip_M_Hair_001_a") },
            { 22, new KeyValuePair<string, string>("multiplayer_overlays", "FM_M_Hair_001_a") },
        };
        private readonly List<string> overlayColorsList = [];
        private readonly List<string> blemishesStyleList = [];
        private readonly List<string> beardStylesList = [];
        private readonly List<string> eyebrowsStyleList = [];
        private readonly List<string> ageingStyleList = [];
        private readonly List<string> makeupStyleList = [];
        private readonly List<string> blushStyleList = [];
        private readonly List<string> complexionStyleList = [];
        private readonly List<string> sunDamageStyleList = [];
        private readonly List<string> lipstickStyleList = [];
        private readonly List<string> molesFrecklesStyleList = [];
        private readonly List<string> chestHairStyleList = [];
        private readonly List<string> bodyBlemishesList = [];


        private readonly Random _random = new Random();
        // TODO: Chris: Replace with enums or something more sane - updating with index/magic numbers is nuts
        private readonly Dictionary<int, Tuple<int, int, float>> appearanceValues = [];
        private int _hairSelection;
        private int _hairColorSelection;
        private int _hairHighlightColorSelection;
        private int _eyeColorSelection;
        private int _facialExpressionSelection;

        private MultiplayerPedData currentCharacter = new();

        // Slots of the character on the player ped that couldn't be matched when it was put on.
        // Captures that only record the current look (the after-death snapshot) keep the saved
        // entry for these instead of whatever the ped shows there.
        private HashSet<ClothingSlot> unresolvedSlots = new();
        private MpCharacterCategory currentCategory = new();

        private Ped _clone;

        /// <summary>
        /// Makes or updates the character creator menu. Also has an option to load data from the <see cref="currentCharacter"/> data, to allow for editing an existing ped.
        /// </summary>
        /// <param name="male"></param>
        /// <param name="editPed"></param>
        private void MakeCreateCharacterMenu(bool male, bool editPed = false)
        {
            isEdidtingPed = editPed;
            if (!editPed)
            {
                currentCharacter = new MultiplayerPedData();
                currentCharacter.DrawableVariations.clothes = new Dictionary<int, KeyValuePair<int, int>>();
                currentCharacter.PropVariations.props = new Dictionary<int, KeyValuePair<int, int>>();
                currentCharacter.PedHeadBlendData = Game.PlayerPed.GetHeadBlendData();
                currentCharacter.Version = 1;
                currentCharacter.ModelHash = male ? (uint)GetHashKey("mp_m_freemode_01") : (uint)GetHashKey("mp_f_freemode_01");
                currentCharacter.IsMale = male;
                unresolvedSlots.Clear();
                faceSelection.Reset(male);
                currentCharacter.PedHeadBlendData = Game.PlayerPed.GetHeadBlendData();

                SetPlayerClothing();
            }


            currentCharacter.DrawableVariations.clothes ??= new Dictionary<int, KeyValuePair<int, int>>();
            currentCharacter.PropVariations.props ??= new Dictionary<int, KeyValuePair<int, int>>();

            // Set the facial expression to default in case it doesn't exist yet, or keep the current one if it does.
            currentCharacter.FacialExpression ??= facial_expressions[0];

            // Set the facial expression on the ped itself.
            SetFacialIdleAnimOverride(Game.PlayerPed.Handle, currentCharacter.FacialExpression ?? facial_expressions[0], null);

            // Set the facial expression item list to the correct saved index.
            if (createCharacterMenu.GetMenuItems().ElementAt(6) is MenuListItem li)
            {
                var index = facial_expressions.IndexOf(currentCharacter.FacialExpression ?? facial_expressions[0]);
                if (index < 0)
                {
                    index = 0;
                }
                li.ListIndex = index;
            }

            appearanceMenu.ClearMenuItems();
            tattoosMenu.ClearMenuItems();
            clothesMenu.ClearMenuItems();
            propsMenu.ClearMenuItems();

            #region appearance menu.
            if (!editPed)
            {
                // Clears any saved appearance values from prior peds
                _hairSelection = 0;
                _hairColorSelection = 0;
                _hairHighlightColorSelection = 0;
                _eyeColorSelection = 0;

                for (int i = 0; i < 12; i++)
                {
                    appearanceValues[i] = new Tuple<int, int, float>(0, 0, 0f);
                }
            }
            else
            {
                PedAppearance appearanceData = currentCharacter.PedAppearance;

                _hairSelection = appearanceData.hairStyle;
                _hairColorSelection = appearanceData.hairColor;
                _hairHighlightColorSelection = appearanceData.hairHighlightColor;

                appearanceValues[0] = new(appearanceData.blemishesStyle, 0, appearanceData.blemishesOpacity);
                appearanceValues[1] = new(appearanceData.beardStyle, appearanceData.beardColor, appearanceData.beardOpacity);
                appearanceValues[2] = new(appearanceData.eyebrowsStyle, appearanceData.eyebrowsColor, appearanceData.eyebrowsOpacity);
                appearanceValues[3] = new(appearanceData.ageingStyle, 0, appearanceData.ageingOpacity);
                appearanceValues[4] = new(appearanceData.makeupStyle, appearanceData.makeupColor, appearanceData.makeupOpacity);
                appearanceValues[5] = new(appearanceData.blushStyle, appearanceData.blushColor, appearanceData.blushOpacity);
                appearanceValues[6] = new(appearanceData.complexionStyle, 0, appearanceData.complexionOpacity);
                appearanceValues[7] = new(appearanceData.sunDamageStyle, 0, appearanceData.sunDamageOpacity);
                appearanceValues[8] = new(appearanceData.lipstickStyle, appearanceData.lipstickColor, appearanceData.lipstickOpacity);
                appearanceValues[9] = new(appearanceData.molesFrecklesStyle, 0, appearanceData.molesFrecklesOpacity);
                appearanceValues[10] = new(appearanceData.chestHairStyle, appearanceData.chestHairColor, appearanceData.chestHairOpacity);
                appearanceValues[11] = new(appearanceData.bodyBlemishesStyle, 0, appearanceData.bodyBlemishesOpacity);

                _eyeColorSelection = appearanceData.eyeColor;
            }

            var opacity = new List<string>() { "0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" };

            var maxHairStyles = GetNumberOfPedDrawableVariations(Game.PlayerPed.Handle, 2);
            //if (currentCharacter.ModelHash == (uint)PedHash.FreemodeFemale01)
            //{
            //    maxHairStyles /= 2;
            //}
            var hairStylesList = new List<string>();
            for (var i = 0; i < maxHairStyles; i++)
            {
                hairStylesList.Add($"Style #{i + 1}");
            }
            hairStylesList.Add($"Style #{maxHairStyles + 1}");

            var eyeColorList = new List<string>();
            for (var i = 0; i < 32; i++)
            {
                eyeColorList.Add($"Eye Color #{i + 1}");
            }

            /*

            0               Blemishes             0 - 23,   255  
            1               Facial Hair           0 - 28,   255  
            2               Eyebrows              0 - 33,   255  
            3               Ageing                0 - 14,   255  
            4               Makeup                0 - 74,   255  
            5               Blush                 0 - 6,    255  
            6               Complexion            0 - 11,   255  
            7               Sun Damage            0 - 10,   255  
            8               Lipstick              0 - 9,    255  
            9               Moles/Freckles        0 - 17,   255  
            10              Chest Hair            0 - 16,   255  
            11              Body Blemishes        0 - 11,   255  
            12              Add Body Blemishes    0 - 1,    255  
            
            */


            // hair
            var currentHairStyle = editPed ? currentCharacter.PedAppearance.hairStyle : GetPedDrawableVariation(Game.PlayerPed.Handle, 2);
            var currentHairColor = editPed ? currentCharacter.PedAppearance.hairColor : 0;
            var currentHairHighlightColor = editPed ? currentCharacter.PedAppearance.hairHighlightColor : 0;

            // 0 blemishes
            var currentBlemishesStyle = editPed ? currentCharacter.PedAppearance.blemishesStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 0) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 0) : 0;
            var currentBlemishesOpacity = editPed ? currentCharacter.PedAppearance.blemishesOpacity : 0f;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 0, currentBlemishesStyle, currentBlemishesOpacity);

            // 1 beard
            var currentBeardStyle = editPed ? currentCharacter.PedAppearance.beardStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 1) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 1) : 0;
            var currentBeardOpacity = editPed ? currentCharacter.PedAppearance.beardOpacity : 0f;
            var currentBeardColor = editPed ? currentCharacter.PedAppearance.beardColor : 0;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 1, currentBeardStyle, currentBeardOpacity);
            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 1, 1, currentBeardColor, currentBeardColor);

            // 2 eyebrows
            var currentEyebrowStyle = editPed ? currentCharacter.PedAppearance.eyebrowsStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 2) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 2) : 0;
            var currentEyebrowOpacity = editPed ? currentCharacter.PedAppearance.eyebrowsOpacity : 0f;
            var currentEyebrowColor = editPed ? currentCharacter.PedAppearance.eyebrowsColor : 0;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 2, currentEyebrowStyle, currentEyebrowOpacity);
            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 2, 1, currentEyebrowColor, currentEyebrowColor);

            // 3 ageing
            var currentAgeingStyle = editPed ? currentCharacter.PedAppearance.ageingStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 3) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 3) : 0;
            var currentAgeingOpacity = editPed ? currentCharacter.PedAppearance.ageingOpacity : 0f;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 3, currentAgeingStyle, currentAgeingOpacity);

            // 4 makeup
            var currentMakeupStyle = editPed ? currentCharacter.PedAppearance.makeupStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 4) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 4) : 0;
            var currentMakeupOpacity = editPed ? currentCharacter.PedAppearance.makeupOpacity : 0f;
            var currentMakeupColor = editPed ? currentCharacter.PedAppearance.makeupColor : 0;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 4, currentMakeupStyle, currentMakeupOpacity);
            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 4, 2, currentMakeupColor, currentMakeupColor);

            // 5 blush
            var currentBlushStyle = editPed ? currentCharacter.PedAppearance.blushStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 5) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 5) : 0;
            var currentBlushOpacity = editPed ? currentCharacter.PedAppearance.blushOpacity : 0f;
            var currentBlushColor = editPed ? currentCharacter.PedAppearance.blushColor : 0;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 5, currentBlushStyle, currentBlushOpacity);
            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 5, 2, currentBlushColor, currentBlushColor);

            // 6 complexion
            var currentComplexionStyle = editPed ? currentCharacter.PedAppearance.complexionStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 6) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 6) : 0;
            var currentComplexionOpacity = editPed ? currentCharacter.PedAppearance.complexionOpacity : 0f;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 6, currentComplexionStyle, currentComplexionOpacity);

            // 7 sun damage
            var currentSunDamageStyle = editPed ? currentCharacter.PedAppearance.sunDamageStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 7) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 7) : 0;
            var currentSunDamageOpacity = editPed ? currentCharacter.PedAppearance.sunDamageOpacity : 0f;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 7, currentSunDamageStyle, currentSunDamageOpacity);

            // 8 lipstick
            var currentLipstickStyle = editPed ? currentCharacter.PedAppearance.lipstickStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 8) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 8) : 0;
            var currentLipstickOpacity = editPed ? currentCharacter.PedAppearance.lipstickOpacity : 0f;
            var currentLipstickColor = editPed ? currentCharacter.PedAppearance.lipstickColor : 0;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 8, currentLipstickStyle, currentLipstickOpacity);
            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 8, 2, currentLipstickColor, currentLipstickColor);

            // 9 moles/freckles
            var currentMolesFrecklesStyle = editPed ? currentCharacter.PedAppearance.molesFrecklesStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 9) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 9) : 0;
            var currentMolesFrecklesOpacity = editPed ? currentCharacter.PedAppearance.molesFrecklesOpacity : 0f;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 9, currentMolesFrecklesStyle, currentMolesFrecklesOpacity);

            // 10 chest hair
            var currentChesthairStyle = editPed ? currentCharacter.PedAppearance.chestHairStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 10) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 10) : 0;
            var currentChesthairOpacity = editPed ? currentCharacter.PedAppearance.chestHairOpacity : 0f;
            var currentChesthairColor = editPed ? currentCharacter.PedAppearance.chestHairColor : 0;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 10, currentChesthairStyle, currentChesthairOpacity);
            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 10, 1, currentChesthairColor, currentChesthairColor);

            // 11 body blemishes
            var currentBodyBlemishesStyle = editPed ? currentCharacter.PedAppearance.bodyBlemishesStyle : GetPedHeadOverlayValue(Game.PlayerPed.Handle, 11) != 255 ? GetPedHeadOverlayValue(Game.PlayerPed.Handle, 11) : 0;
            var currentBodyBlemishesOpacity = editPed ? currentCharacter.PedAppearance.bodyBlemishesOpacity : 0f;
            SetPedHeadOverlay(Game.PlayerPed.Handle, 11, currentBodyBlemishesStyle, currentBodyBlemishesOpacity);

            var currentEyeColor = editPed ? currentCharacter.PedAppearance.eyeColor : 0;
            SetPedEyeColor(Game.PlayerPed.Handle, currentEyeColor);

            var hairStyles = new MenuListItem("Hair Style", hairStylesList, currentHairStyle, "Select a hair style.");
            //MenuListItem hairColors = new MenuListItem("Hair Color", overlayColorsList, currentHairColor, "Select a hair color.");
            var hairColors = new MenuListItem("Hair Color", overlayColorsList, currentHairColor, "Select a hair color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Hair };
            //MenuListItem hairHighlightColors = new MenuListItem("Hair Highlight Color", overlayColorsList, currentHairHighlightColor, "Select a hair highlight color.");
            var hairHighlightColors = new MenuListItem("Hair Highlight Color", overlayColorsList, currentHairHighlightColor, "Select a hair highlight color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Hair };

            var blemishesStyle = new MenuListItem("Blemishes Style", blemishesStyleList, currentBlemishesStyle, "Select a blemishes style.");
            //MenuSliderItem blemishesOpacity = new MenuSliderItem("Blemishes Opacity", "Select a blemishes opacity.", 0, 10, (int)(currentBlemishesOpacity * 10f), false);
            var blemishesOpacity = new MenuListItem("Blemishes Opacity", opacity, (int)(currentBlemishesOpacity * 10f), "Select a blemishes opacity.") { ShowOpacityPanel = true };

            var beardStyles = new MenuListItem("Beard Style", beardStylesList, currentBeardStyle, "Select a beard/facial hair style.");
            var beardOpacity = new MenuListItem("Beard Opacity", opacity, (int)(currentBeardOpacity * 10f), "Select the opacity for your beard/facial hair.") { ShowOpacityPanel = true };
            var beardColor = new MenuListItem("Beard Color", overlayColorsList, currentBeardColor, "Select a beard color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Hair };
            //MenuSliderItem beardOpacity = new MenuSliderItem("Beard Opacity", "Select the opacity for your beard/facial hair.", 0, 10, (int)(currentBeardOpacity * 10f), false);
            //MenuListItem beardColor = new MenuListItem("Beard Color", overlayColorsList, currentBeardColor, "Select a beard color");

            var eyebrowStyle = new MenuListItem("Eyebrows Style", eyebrowsStyleList, currentEyebrowStyle, "Select an eyebrows style.");
            var eyebrowOpacity = new MenuListItem("Eyebrows Opacity", opacity, (int)(currentEyebrowOpacity * 10f), "Select the opacity for your eyebrows.") { ShowOpacityPanel = true };
            var eyebrowColor = new MenuListItem("Eyebrows Color", overlayColorsList, currentEyebrowColor, "Select an eyebrows color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Hair };
            //MenuSliderItem eyebrowOpacity = new MenuSliderItem("Eyebrows Opacity", "Select the opacity for your eyebrows.", 0, 10, (int)(currentEyebrowOpacity * 10f), false);

            var ageingStyle = new MenuListItem("Ageing Style", ageingStyleList, currentAgeingStyle, "Select an ageing style.");
            var ageingOpacity = new MenuListItem("Ageing Opacity", opacity, (int)(currentAgeingOpacity * 10f), "Select an ageing opacity.") { ShowOpacityPanel = true };
            //MenuSliderItem ageingOpacity = new MenuSliderItem("Ageing Opacity", "Select an ageing opacity.", 0, 10, (int)(currentAgeingOpacity * 10f), false);

            var makeupStyle = new MenuListItem("Makeup Style", makeupStyleList, currentMakeupStyle, "Select a makeup style.");
            var makeupOpacity = new MenuListItem("Makeup Opacity", opacity, (int)(currentMakeupOpacity * 10f), "Select a makeup opacity") { ShowOpacityPanel = true };
            //MenuSliderItem makeupOpacity = new MenuSliderItem("Makeup Opacity", 0, 10, (int)(currentMakeupOpacity * 10f), "Select a makeup opacity.");
            var makeupColor = new MenuListItem("Makeup Color", overlayColorsList, currentMakeupColor, "Select a makeup color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Makeup };

            var blushStyle = new MenuListItem("Blush Style", blushStyleList, currentBlushStyle, "Select a blush style.");
            var blushOpacity = new MenuListItem("Blush Opacity", opacity, (int)(currentBlushOpacity * 10f), "Select a blush opacity.") { ShowOpacityPanel = true };
            //MenuSliderItem blushOpacity = new MenuSliderItem("Blush Opacity", 0, 10, (int)(currentBlushOpacity * 10f), "Select a blush opacity.");
            var blushColor = new MenuListItem("Blush Color", overlayColorsList, currentBlushColor, "Select a blush color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Makeup };

            var complexionStyle = new MenuListItem("Complexion Style", complexionStyleList, currentComplexionStyle, "Select a complexion style.");
            //MenuSliderItem complexionOpacity = new MenuSliderItem("Complexion Opacity", 0, 10, (int)(currentComplexionOpacity * 10f), "Select a complexion opacity.");
            var complexionOpacity = new MenuListItem("Complexion Opacity", opacity, (int)(currentComplexionOpacity * 10f), "Select a complexion opacity.") { ShowOpacityPanel = true };

            var sunDamageStyle = new MenuListItem("Sun Damage Style", sunDamageStyleList, currentSunDamageStyle, "Select a sun damage style.");
            //MenuSliderItem sunDamageOpacity = new MenuSliderItem("Sun Damage Opacity", 0, 10, (int)(currentSunDamageOpacity * 10f), "Select a sun damage opacity.");
            var sunDamageOpacity = new MenuListItem("Sun Damage Opacity", opacity, (int)(currentSunDamageOpacity * 10f), "Select a sun damage opacity.") { ShowOpacityPanel = true };

            var lipstickStyle = new MenuListItem("Lipstick Style", lipstickStyleList, currentLipstickStyle, "Select a lipstick style.");
            //MenuSliderItem lipstickOpacity = new MenuSliderItem("Lipstick Opacity", 0, 10, (int)(currentLipstickOpacity * 10f), "Select a lipstick opacity.");
            var lipstickOpacity = new MenuListItem("Lipstick Opacity", opacity, (int)(currentLipstickOpacity * 10f), "Select a lipstick opacity.") { ShowOpacityPanel = true };
            var lipstickColor = new MenuListItem("Lipstick Color", overlayColorsList, currentLipstickColor, "Select a lipstick color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Makeup };

            var molesFrecklesStyle = new MenuListItem("Moles and Freckles Style", molesFrecklesStyleList, currentMolesFrecklesStyle, "Select a moles and freckles style.");
            //MenuSliderItem molesFrecklesOpacity = new MenuSliderItem("Moles and Freckles Opacity", 0, 10, (int)(currentMolesFrecklesOpacity * 10f), "Select a moles and freckles opacity.");
            var molesFrecklesOpacity = new MenuListItem("Moles and Freckles Opacity", opacity, (int)(currentMolesFrecklesOpacity * 10f), "Select a moles and freckles opacity.") { ShowOpacityPanel = true };

            var chestHairStyle = new MenuListItem("Chest Hair Style", chestHairStyleList, currentChesthairStyle, "Select a chest hair style.");
            //MenuSliderItem chestHairOpacity = new MenuSliderItem("Chest Hair Opacity", 0, 10, (int)(currentChesthairOpacity * 10f), "Select a chest hair opacity.");
            var chestHairOpacity = new MenuListItem("Chest Hair Opacity", opacity, (int)(currentChesthairOpacity * 10f), "Select a chest hair opacity.") { ShowOpacityPanel = true };
            var chestHairColor = new MenuListItem("Chest Hair Color", overlayColorsList, currentChesthairColor, "Select a chest hair color.") { ShowColorPanel = true, ColorPanelColorType = MenuListItem.ColorPanelType.Hair };

            // Body blemishes
            var bodyBlemishesStyle = new MenuListItem("Body Blemishes Style", bodyBlemishesList, currentBodyBlemishesStyle, "Select body blemishes style.");
            var bodyBlemishesOpacity = new MenuListItem("Body Blemishes Opacity", opacity, (int)(currentBodyBlemishesOpacity * 10f), "Select body blemishes opacity.") { ShowOpacityPanel = true };

            var eyeColor = new MenuListItem("Eye Colors", eyeColorList, currentEyeColor, "Select an eye/contact lens color.");

            appearanceMenu.AddMenuItem(hairStyles);
            appearanceMenu.AddMenuItem(hairColors);
            appearanceMenu.AddMenuItem(hairHighlightColors);

            appearanceMenu.AddMenuItem(blemishesStyle);
            appearanceMenu.AddMenuItem(blemishesOpacity);

            appearanceMenu.AddMenuItem(beardStyles);
            appearanceMenu.AddMenuItem(beardOpacity);
            appearanceMenu.AddMenuItem(beardColor);

            appearanceMenu.AddMenuItem(eyebrowStyle);
            appearanceMenu.AddMenuItem(eyebrowOpacity);
            appearanceMenu.AddMenuItem(eyebrowColor);

            appearanceMenu.AddMenuItem(ageingStyle);
            appearanceMenu.AddMenuItem(ageingOpacity);

            appearanceMenu.AddMenuItem(makeupStyle);
            appearanceMenu.AddMenuItem(makeupOpacity);
            appearanceMenu.AddMenuItem(makeupColor);

            appearanceMenu.AddMenuItem(blushStyle);
            appearanceMenu.AddMenuItem(blushOpacity);
            appearanceMenu.AddMenuItem(blushColor);

            appearanceMenu.AddMenuItem(complexionStyle);
            appearanceMenu.AddMenuItem(complexionOpacity);

            appearanceMenu.AddMenuItem(sunDamageStyle);
            appearanceMenu.AddMenuItem(sunDamageOpacity);

            appearanceMenu.AddMenuItem(lipstickStyle);
            appearanceMenu.AddMenuItem(lipstickOpacity);
            appearanceMenu.AddMenuItem(lipstickColor);

            appearanceMenu.AddMenuItem(molesFrecklesStyle);
            appearanceMenu.AddMenuItem(molesFrecklesOpacity);

            appearanceMenu.AddMenuItem(chestHairStyle);
            appearanceMenu.AddMenuItem(chestHairOpacity);
            appearanceMenu.AddMenuItem(chestHairColor);

            appearanceMenu.AddMenuItem(bodyBlemishesStyle);
            appearanceMenu.AddMenuItem(bodyBlemishesOpacity);

            appearanceMenu.AddMenuItem(eyeColor);

            if (male)
            {
                // There are weird people out there that wanted makeup for male characters
                // so yeah.... here you go I suppose... strange...

                /*
                makeupStyle.Enabled = false;
                makeupStyle.LeftIcon = MenuItem.Icon.LOCK;
                makeupStyle.Description = "This is not available for male characters.";

                makeupOpacity.Enabled = false;
                makeupOpacity.LeftIcon = MenuItem.Icon.LOCK;
                makeupOpacity.Description = "This is not available for male characters.";

                makeupColor.Enabled = false;
                makeupColor.LeftIcon = MenuItem.Icon.LOCK;
                makeupColor.Description = "This is not available for male characters.";


                blushStyle.Enabled = false;
                blushStyle.LeftIcon = MenuItem.Icon.LOCK;
                blushStyle.Description = "This is not available for male characters.";

                blushOpacity.Enabled = false;
                blushOpacity.LeftIcon = MenuItem.Icon.LOCK;
                blushOpacity.Description = "This is not available for male characters.";

                blushColor.Enabled = false;
                blushColor.LeftIcon = MenuItem.Icon.LOCK;
                blushColor.Description = "This is not available for male characters.";


                lipstickStyle.Enabled = false;
                lipstickStyle.LeftIcon = MenuItem.Icon.LOCK;
                lipstickStyle.Description = "This is not available for male characters.";

                lipstickOpacity.Enabled = false;
                lipstickOpacity.LeftIcon = MenuItem.Icon.LOCK;
                lipstickOpacity.Description = "This is not available for male characters.";

                lipstickColor.Enabled = false;
                lipstickColor.LeftIcon = MenuItem.Icon.LOCK;
                lipstickColor.Description = "This is not available for male characters.";
                */
            }
            else
            {
                beardStyles.Enabled = false;
                beardStyles.LeftIcon = MenuItem.Icon.LOCK;
                beardStyles.Description = "This is not available for female characters.";

                beardOpacity.Enabled = false;
                beardOpacity.LeftIcon = MenuItem.Icon.LOCK;
                beardOpacity.Description = "This is not available for female characters.";

                beardColor.Enabled = false;
                beardColor.LeftIcon = MenuItem.Icon.LOCK;
                beardColor.Description = "This is not available for female characters.";


                chestHairStyle.Enabled = false;
                chestHairStyle.LeftIcon = MenuItem.Icon.LOCK;
                chestHairStyle.Description = "This is not available for female characters.";

                chestHairOpacity.Enabled = false;
                chestHairOpacity.LeftIcon = MenuItem.Icon.LOCK;
                chestHairOpacity.Description = "This is not available for female characters.";

                chestHairColor.Enabled = false;
                chestHairColor.LeftIcon = MenuItem.Icon.LOCK;
                chestHairColor.Description = "This is not available for female characters.";
            }

            #endregion

            #region clothing options menu
            var clothingCategoryNames = new string[12] { "Unused (head)", "Masks", "Unused (hair)", "Upper Body", "Lower Body", "Bags & Parachutes", "Shoes", "Scarfs & Chains", "Shirt & Accessory", "Body Armor & Accessory 2", "Badges & Logos", "Shirt Overlay & Jackets" };
            for (var i = 0; i < 12; i++)
            {
                if (i is not 0 and not 2)
                {
                    // The ped was just dressed from the save (Edit spawns it first), so it shows the
                    // current number of every matched piece; saved numbers can be from an older layout.
                    var currentVariationIndex = GetPedDrawableVariation(Game.PlayerPed.Handle, i);
                    var currentVariationTextureIndex = GetPedTextureVariation(Game.PlayerPed.Handle, i);

                    var maxDrawables = GetNumberOfPedDrawableVariations(Game.PlayerPed.Handle, i);

                    var items = new List<string>();
                    for (var x = 0; x < maxDrawables; x++)
                    {
                        items.Add($"Drawable #{x} (of {maxDrawables})");
                    }

                    var maxTextures = GetNumberOfPedTextureVariations(Game.PlayerPed.Handle, i, currentVariationIndex);

                    var listItem = new MenuListItem(clothingCategoryNames[i], items, currentVariationIndex, $"Select a drawable using the arrow keys and press ~o~enter~s~ to cycle through all available textures. Currently selected texture: #{currentVariationTextureIndex + 1} (of {maxTextures}).");
                    clothesMenu.AddMenuItem(listItem);
                }
            }
            #endregion

            #region props options menu
            var propNames = new string[5] { "Hats & Helmets", "Glasses", "Misc Props", "Watches", "Bracelets" };
            for (var x = 0; x < 5; x++)
            {
                var propId = x;
                if (x > 2)
                {
                    propId += 3;
                }

                var currentProp = GetPedPropIndex(Game.PlayerPed.Handle, propId);
                var currentPropTexture = GetPedPropTextureIndex(Game.PlayerPed.Handle, propId);

                var propsList = new List<string>();
                for (var i = 0; i < GetNumberOfPedPropDrawableVariations(Game.PlayerPed.Handle, propId); i++)
                {
                    propsList.Add($"Prop #{i} (of {GetNumberOfPedPropDrawableVariations(Game.PlayerPed.Handle, propId)})");
                }
                propsList.Add("No Prop");


                if (GetPedPropIndex(Game.PlayerPed.Handle, propId) != -1)
                {
                    var maxPropTextures = GetNumberOfPedPropTextureVariations(Game.PlayerPed.Handle, propId, currentProp);
                    var propListItem = new MenuListItem($"{propNames[x]}", propsList, currentProp, $"Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures. Currently selected texture: #{currentPropTexture + 1} (of {maxPropTextures}).");
                    propsMenu.AddMenuItem(propListItem);
                }
                else
                {
                    var propListItem = new MenuListItem($"{propNames[x]}", propsList, currentProp, "Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures.");
                    propsMenu.AddMenuItem(propListItem);
                }


            }
            #endregion

            #region face features menu
            foreach (var item in faceShapeMenu.GetMenuItems().OfType<MenuSliderItem>())
            {
                var features = editPed ? currentCharacter.FaceShapeFeatures.features : null;
                item.Position = FaceFeatureValues.GetSliderPosition(features, item.Index);
                SetPedFaceFeature(Game.PlayerPed.Handle, item.Index, FaceFeatureValues.GetValue(features, item.Index));
            }
            #endregion

            #region Tattoos menu
            var headTattoosList = new List<string>();
            var torsoTattoosList = new List<string>();
            var leftArmTattoosList = new List<string>();
            var rightArmTattoosList = new List<string>();
            var leftLegTattoosList = new List<string>();
            var rightLegTattoosList = new List<string>();
            var badgeTattoosList = new List<string>();

            TattoosData.GenerateTattoosData();
            if (male)
            {
                var counter = 1;
                foreach (var tattoo in MaleTattoosCollection.HEAD)
                {
                    headTattoosList.Add($"Tattoo #{counter} (of {MaleTattoosCollection.HEAD.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in MaleTattoosCollection.TORSO)
                {
                    torsoTattoosList.Add($"Tattoo #{counter} (of {MaleTattoosCollection.TORSO.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in MaleTattoosCollection.LEFT_ARM)
                {
                    leftArmTattoosList.Add($"Tattoo #{counter} (of {MaleTattoosCollection.LEFT_ARM.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in MaleTattoosCollection.RIGHT_ARM)
                {
                    rightArmTattoosList.Add($"Tattoo #{counter} (of {MaleTattoosCollection.RIGHT_ARM.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in MaleTattoosCollection.LEFT_LEG)
                {
                    leftLegTattoosList.Add($"Tattoo #{counter} (of {MaleTattoosCollection.LEFT_LEG.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in MaleTattoosCollection.RIGHT_LEG)
                {
                    rightLegTattoosList.Add($"Tattoo #{counter} (of {MaleTattoosCollection.RIGHT_LEG.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in MaleTattoosCollection.BADGES)
                {
                    badgeTattoosList.Add($"Badge #{counter} (of {MaleTattoosCollection.BADGES.Count})");
                    counter++;
                }
            }
            else
            {
                var counter = 1;
                foreach (var tattoo in FemaleTattoosCollection.HEAD)
                {
                    headTattoosList.Add($"Tattoo #{counter} (of {FemaleTattoosCollection.HEAD.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in FemaleTattoosCollection.TORSO)
                {
                    torsoTattoosList.Add($"Tattoo #{counter} (of {FemaleTattoosCollection.TORSO.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in FemaleTattoosCollection.LEFT_ARM)
                {
                    leftArmTattoosList.Add($"Tattoo #{counter} (of {FemaleTattoosCollection.LEFT_ARM.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in FemaleTattoosCollection.RIGHT_ARM)
                {
                    rightArmTattoosList.Add($"Tattoo #{counter} (of {FemaleTattoosCollection.RIGHT_ARM.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in FemaleTattoosCollection.LEFT_LEG)
                {
                    leftLegTattoosList.Add($"Tattoo #{counter} (of {FemaleTattoosCollection.LEFT_LEG.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in FemaleTattoosCollection.RIGHT_LEG)
                {
                    rightLegTattoosList.Add($"Tattoo #{counter} (of {FemaleTattoosCollection.RIGHT_LEG.Count})");
                    counter++;
                }
                counter = 1;
                foreach (var tattoo in FemaleTattoosCollection.BADGES)
                {
                    badgeTattoosList.Add($"Badge #{counter} (of {FemaleTattoosCollection.BADGES.Count})");
                    counter++;
                }
            }

            const string tatDesc = "Cycle through the list to preview tattoos. If you like one, press enter to select it, selecting it will add the tattoo if you don't already have it. If you already have that tattoo then the tattoo will be removed.";
            var headTatts = new MenuListItem("Head Tattoos", headTattoosList, 0, tatDesc);
            var torsoTatts = new MenuListItem("Torso Tattoos", torsoTattoosList, 0, tatDesc);
            var leftArmTatts = new MenuListItem("Left Arm Tattoos", leftArmTattoosList, 0, tatDesc);
            var rightArmTatts = new MenuListItem("Right Arm Tattoos", rightArmTattoosList, 0, tatDesc);
            var leftLegTatts = new MenuListItem("Left Leg Tattoos", leftLegTattoosList, 0, tatDesc);
            var rightLegTatts = new MenuListItem("Right Leg Tattoos", rightLegTattoosList, 0, tatDesc);
            var badgeTatts = new MenuListItem("Badge Overlays", badgeTattoosList, 0, tatDesc);

            tattoosMenu.AddMenuItem(headTatts);
            tattoosMenu.AddMenuItem(torsoTatts);
            tattoosMenu.AddMenuItem(leftArmTatts);
            tattoosMenu.AddMenuItem(rightArmTatts);
            tattoosMenu.AddMenuItem(leftLegTatts);
            tattoosMenu.AddMenuItem(rightLegTatts);
            tattoosMenu.AddMenuItem(badgeTatts);
            tattoosMenu.AddMenuItem(new MenuItem("Remove All Tattoos", "Click this if you want to remove all tattoos and start over."));
            #endregion

            List<string> categoryNames = GetAllCategoryNames();

            categoryNames.RemoveAt(0);

            List<MenuItem.Icon> categoryIcons = GetCategoryIcons(categoryNames);

            categoryBtn.ItemData = new Tuple<List<string>, List<MenuItem.Icon>>(categoryNames, categoryIcons);
            categoryBtn.ListItems = categoryNames;            

            if (editPed)
            {
                int characterCategoryIndex = categoryNames.IndexOf(currentCharacter.Category);

                categoryBtn.ListIndex = characterCategoryIndex;
            }
            else
            {
                categoryBtn.ListIndex = 0;
            }

            categoryBtn.RightIcon = categoryIcons[categoryBtn.ListIndex];

            createCharacterMenu.RefreshIndex();
            appearanceMenu.RefreshIndex();
            inheritanceMenu.RefreshIndex();
            tattoosMenu.RefreshIndex();
        }

        /// <summary>Records what the player ped now wears in one slot, after the editor changed it.</summary>
        private void RecordEditedSlot(ClothingSlot slot)
        {
            currentCharacter = ClothingService.SetCharacterSlot(currentCharacter, Game.PlayerPed.Handle, slot);
            unresolvedSlots.Remove(slot);
        }

        /// <summary>
        /// Saves the mp character and quits the editor if successful.
        /// </summary>
        /// <returns></returns>
        private async Task<bool> SavePed()
        {
            currentCharacter.PedHeadBlendData = Game.PlayerPed.GetHeadBlendData();
            // Slots the editor never touched (a new character's defaults) get references from the ped.
            // Unmatched pieces of a loaded character keep their saved entry.
            currentCharacter = ClothingService.CompleteFromPed(currentCharacter, Game.PlayerPed.Handle);
            if (isEdidtingPed)
            {
                var json = JsonConvert.SerializeObject(currentCharacter);
                if (StorageManager.SaveJsonData(currentCharacter.SaveName, json, true))
                {
                    Notify.Success("Your character was saved successfully.");
                    return true;
                }
                else
                {
                    Notify.Error("Your character could not be saved. Reason unknown. :(");
                    return false;
                }
            }
            else
            {
                var name = await GetUserInput(windowTitle: "Enter a save name.", maxInputLength: 30);
                if (string.IsNullOrEmpty(name))
                {
                    Notify.Error(CommonErrors.InvalidInput);
                    return false;
                }
                else
                {
                    currentCharacter.SaveName = "mp_ped_" + name;
                    var json = JsonConvert.SerializeObject(currentCharacter);

                    if (StorageManager.SaveJsonData("mp_ped_" + name, json, false))
                    {
                        Notify.Success($"Your character (~g~{name}~s~) has been saved.");
                        Log($"Saved Character {name}. Data: {json}");
                        return true;
                    }
                    else
                    {
                        Notify.Error($"Saving failed, most likely because this name (~y~{name}~s~) is already in use.");
                        return false;
                    }
                }
            }

        }

        /// <summary>
        /// Creates the menu.
        /// </summary>
        private void CreateMenu()
        {
            for (int i = 0; i < GetNumHairColors(); i++)
            {
                overlayColorsList.Add($"Color #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(0); i++)
            {
                blemishesStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(1); i++)
            {
                beardStylesList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(2); i++)
            {
                eyebrowsStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(3); i++)
            {
                ageingStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(4); i++)
            {
                makeupStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(5); i++)
            {
                blushStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(6); i++)
            {
                complexionStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(7); i++)
            {
                sunDamageStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(8); i++)
            {
                lipstickStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(9); i++)
            {
                molesFrecklesStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(10); i++)
            {
                chestHairStyleList.Add($"Style #{i + 1}");
            }

            for (int i = 0; i < GetNumHeadOverlayValues(11); i++)
            {
                bodyBlemishesList.Add($"Style #{i + 1}");
            }

            // Create the menu.
            menu = new Menu(" ", "MP Ped Customization");

            var savedCharacters = new MenuItem("Saved Characters", "Spawn, edit or delete your existing saved multiplayer characters.")
            {
                Label = "→→→"
            };

            MenuController.AddMenu(createCharacterMenu);
            MenuController.AddMenu(savedCharactersMenu);
            MenuController.AddMenu(savedCharactersCategoryMenu);
            MenuController.AddMenu(inheritanceMenu);
            MenuController.AddMenu(appearanceMenu);
            MenuController.AddMenu(faceShapeMenu);
            MenuController.AddMenu(tattoosMenu);
            MenuController.AddMenu(clothesMenu);
            MenuController.AddMenu(propsMenu);

            CreateSavedPedsMenu();

            menu.AddMenuItem(createMaleBtn);
            MenuController.BindMenuItem(menu, createCharacterMenu, createMaleBtn);
            menu.AddMenuItem(createFemaleBtn);
            MenuController.BindMenuItem(menu, createCharacterMenu, createFemaleBtn);
            menu.AddMenuItem(savedCharacters);
            MenuController.BindMenuItem(menu, savedCharactersMenu, savedCharacters);

            var savedOutfitsMenu = new Menu(" ", "Saved Outfits");
            var savedOutfits = new MenuItem("Saved Outfits", "Save the clothes you're wearing as an outfit, and change into a saved one. Your face, hair and tattoos stay as they are.")
            {
                Label = "→→→"
            };
            MenuController.AddSubmenu(menu, savedOutfitsMenu);
            menu.AddMenuItem(savedOutfits);
            MenuController.BindMenuItem(menu, savedOutfitsMenu, savedOutfits);
            CreateSavedOutfitsMenu(savedOutfitsMenu);

            // setr vmenu_dept_outfits true; the server decides what each player sees and may change.
            if (DepartmentOutfitsBridge.Enabled)
            {
                var deptOutfitsMenu = new Menu(" ", "Department Outfits");
                var deptOutfits = new MenuItem("Department Outfits", "Outfits your department publishes, for the department you're on duty with. Department heads publish and manage them here.")
                {
                    Label = "→→→"
                };
                MenuController.AddSubmenu(menu, deptOutfitsMenu);
                menu.AddMenuItem(deptOutfits);
                MenuController.BindMenuItem(menu, deptOutfitsMenu, deptOutfits);
                CreateDepartmentOutfitsMenu(deptOutfitsMenu);
            }

            menu.RefreshIndex();

            foreach (var creatorMenu in new[] { createCharacterMenu, inheritanceMenu, appearanceMenu, faceShapeMenu, tattoosMenu, clothesMenu, propsMenu })
            {
                MenuKeys.AddControlHints(creatorMenu,
                    new MenuKeys.Hint(Control.MoveLeftRight, "Turn Head"),
                    new MenuKeys.Hint(Control.PhoneExtraOption, "Turn Character"),
                    new MenuKeys.Hint(Control.ParachuteBrakeRight, "Turn Camera Right"),
                    new MenuKeys.Hint(Control.ParachuteBrakeLeft, "Turn Camera Left"));
            }


            var randomizeButton = new MenuItem("Randomize Character", "Randomize character appearance.");
            var inheritanceButton = new MenuItem("Face & Skin", "Pick a face and skin tone directly, or make an optional custom blend.");
            var appearanceButton = new MenuItem("Character Appearance", "Character appearance options.");
            var faceButton = new MenuItem("Character Face Shape Options", "Character face shape options.");
            var tattoosButton = new MenuItem("Character Tattoo Options", "Character tattoo options.");
            var clothesButton = new MenuItem("Character Clothes", "Character clothes.");
            var propsButton = new MenuItem("Character Props", "Character props.");
            var loadOutfitCodeButton = new MenuItem("Load Shared Outfit", "Duplicates your character ready for outfit sharing.");
            var generateOutfitCodeButton = new MenuItem("Generate Outfit Code", "Generates a code for your character ready for outfit sharing.");

            if(!OutfitCodesEnabled)
            {
                loadOutfitCodeButton.Enabled = false;
                loadOutfitCodeButton.RightIcon = MenuItem.Icon.LOCK;
                loadOutfitCodeButton.Description = "~r~Outfit Code Sharing is disabled by the Server Owner.";
                generateOutfitCodeButton.Enabled = false;
                generateOutfitCodeButton.RightIcon = MenuItem.Icon.LOCK;
                generateOutfitCodeButton.Description = "~r~Outfit Code Sharing is disabled by the Server Owner.";
            }

            var saveButton = new MenuItem("Save Character", "Save your character.");
            var exitNoSave = new MenuItem("Exit Without Saving", "Are you sure? All unsaved work will be lost.");
            var faceExpressionList = new MenuListItem("Facial Expression", new List<string> { "Normal", "Happy", "Angry", "Aiming", "Injured", "Stressed", "Smug", "Sulk" }, 0, "Set a facial expression that will be used whenever your ped is idling.");

            inheritanceButton.Label = "→→→";
            appearanceButton.Label = "→→→";
            faceButton.Label = "→→→";
            tattoosButton.Label = "→→→";
            clothesButton.Label = "→→→";
            propsButton.Label = "→→→";

            createCharacterMenu.AddMenuItem(randomizeButton);
            createCharacterMenu.AddMenuItem(inheritanceButton);
            createCharacterMenu.AddMenuItem(appearanceButton);
            createCharacterMenu.AddMenuItem(faceButton);
            createCharacterMenu.AddMenuItem(tattoosButton);
            createCharacterMenu.AddMenuItem(clothesButton);
            createCharacterMenu.AddMenuItem(propsButton);
            
            createCharacterMenu.AddMenuItem(loadOutfitCodeButton);
            createCharacterMenu.AddMenuItem(generateOutfitCodeButton);
            createCharacterMenu.AddMenuItem(faceExpressionList);
            createCharacterMenu.AddMenuItem(categoryBtn);
            createCharacterMenu.AddMenuItem(saveButton);
            createCharacterMenu.AddMenuItem(exitNoSave);

            MenuController.BindMenuItem(createCharacterMenu, inheritanceMenu, inheritanceButton);
            MenuController.BindMenuItem(createCharacterMenu, appearanceMenu, appearanceButton);
            MenuController.BindMenuItem(createCharacterMenu, faceShapeMenu, faceButton);
            MenuController.BindMenuItem(createCharacterMenu, tattoosMenu, tattoosButton);
            MenuController.BindMenuItem(createCharacterMenu, clothesMenu, clothesButton);
            MenuController.BindMenuItem(createCharacterMenu, propsMenu, propsButton);

            faceSelection = new FaceSelectionMenu(inheritanceMenu);

            #region appearance
            // manage the list changes for appearance items.
            appearanceMenu.OnListIndexChange += (_menu, listItem, oldSelectionIndex, newSelectionIndex, itemIndex) =>
            {
                if (itemIndex == 0) // hair style
                {
                    ChangePlayerHair(newSelectionIndex);
                }
                else if (itemIndex is 1 or 2) // hair colors
                {
                    var tmp = (MenuListItem)_menu.GetMenuItems()[1];
                    var hairColor = tmp.ListIndex;
                    tmp = (MenuListItem)_menu.GetMenuItems()[2];
                    var hairHighlightColor = tmp.ListIndex;

                    ChangePlayerHairColor(hairColor, hairHighlightColor);

                    currentCharacter.PedAppearance.hairColor = hairColor;
                    currentCharacter.PedAppearance.hairHighlightColor = hairHighlightColor;
                }
                else if (itemIndex == 33) // eye color
                {
                    var selection = ((MenuListItem)_menu.GetMenuItems()[itemIndex]).ListIndex;
                    ChangePlayerEyeColor(selection);
                    currentCharacter.PedAppearance.eyeColor = selection;
                }
                else
                {
                    var selection = ((MenuListItem)_menu.GetMenuItems()[itemIndex]).ListIndex;
                    var opacity = 0f;
                    if (_menu.GetMenuItems()[itemIndex + 1] is MenuListItem item2)
                    {
                        opacity = (((float)item2.ListIndex + 1) / 10f) - 0.1f;
                    }
                    else if (_menu.GetMenuItems()[itemIndex - 1] is MenuListItem item1)
                    {
                        opacity = (((float)item1.ListIndex + 1) / 10f) - 0.1f;
                    }
                    else if (_menu.GetMenuItems()[itemIndex] is MenuListItem item)
                    {
                        opacity = (((float)item.ListIndex + 1) / 10f) - 0.1f;
                    }
                    else
                    {
                        opacity = 1f;
                    }

                    switch (itemIndex)
                    {
                        case 3: // blemishes
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 0, selection, opacity);
                            currentCharacter.PedAppearance.blemishesStyle = selection;
                            currentCharacter.PedAppearance.blemishesOpacity = opacity;
                            break;
                        case 5: // beards
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 1, selection, opacity);
                            currentCharacter.PedAppearance.beardStyle = selection;
                            currentCharacter.PedAppearance.beardOpacity = opacity;
                            break;
                        case 7: // beards color
                            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 1, 1, selection, selection);
                            currentCharacter.PedAppearance.beardColor = selection;
                            break;
                        case 8: // eyebrows
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 2, selection, opacity);
                            currentCharacter.PedAppearance.eyebrowsStyle = selection;
                            currentCharacter.PedAppearance.eyebrowsOpacity = opacity;
                            break;
                        case 10: // eyebrows color
                            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 2, 1, selection, selection);
                            currentCharacter.PedAppearance.eyebrowsColor = selection;
                            break;
                        case 11: // ageing
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 3, selection, opacity);
                            currentCharacter.PedAppearance.ageingStyle = selection;
                            currentCharacter.PedAppearance.ageingOpacity = opacity;
                            break;
                        case 13: // makeup
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 4, selection, opacity);
                            currentCharacter.PedAppearance.makeupStyle = selection;
                            currentCharacter.PedAppearance.makeupOpacity = opacity;
                            break;
                        case 15: // makeup color
                            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 4, 2, selection, selection);
                            currentCharacter.PedAppearance.makeupColor = selection;
                            break;
                        case 16: // blush style
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 5, selection, opacity);
                            currentCharacter.PedAppearance.blushStyle = selection;
                            currentCharacter.PedAppearance.blushOpacity = opacity;
                            break;
                        case 18: // blush color
                            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 5, 2, selection, selection);
                            currentCharacter.PedAppearance.blushColor = selection;
                            break;
                        case 19: // complexion
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 6, selection, opacity);
                            currentCharacter.PedAppearance.complexionStyle = selection;
                            currentCharacter.PedAppearance.complexionOpacity = opacity;
                            break;
                        case 21: // sun damage
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 7, selection, opacity);
                            currentCharacter.PedAppearance.sunDamageStyle = selection;
                            currentCharacter.PedAppearance.sunDamageOpacity = opacity;
                            break;
                        case 23: // lipstick
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 8, selection, opacity);
                            currentCharacter.PedAppearance.lipstickStyle = selection;
                            currentCharacter.PedAppearance.lipstickOpacity = opacity;
                            break;
                        case 25: // lipstick color
                            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 8, 2, selection, selection);
                            currentCharacter.PedAppearance.lipstickColor = selection;
                            break;
                        case 26: // moles and freckles
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 9, selection, opacity);
                            currentCharacter.PedAppearance.molesFrecklesStyle = selection;
                            currentCharacter.PedAppearance.molesFrecklesOpacity = opacity;
                            break;
                        case 28: // chest hair
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 10, selection, opacity);
                            currentCharacter.PedAppearance.chestHairStyle = selection;
                            currentCharacter.PedAppearance.chestHairOpacity = opacity;
                            break;
                        case 30: // chest hair color
                            SetPedHeadOverlayColor(Game.PlayerPed.Handle, 10, 1, selection, selection);
                            currentCharacter.PedAppearance.chestHairColor = selection;
                            break;
                        case 31: // body blemishes
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 11, selection, opacity);
                            currentCharacter.PedAppearance.bodyBlemishesStyle = selection;
                            currentCharacter.PedAppearance.bodyBlemishesOpacity = opacity;
                            break;
                    }
                }
                RefreshCurrentCharacterSnapshot();
            };

            // manage the slider changes for opacity on the appearance items.
            appearanceMenu.OnListIndexChange += (_menu, listItem, oldSelectionIndex, newSelectionIndex, itemIndex) =>
            {
                if (itemIndex is > 2 and < 33)
                {

                    var selection = ((MenuListItem)_menu.GetMenuItems()[itemIndex - 1]).ListIndex;
                    var opacity = 0f;
                    if (_menu.GetMenuItems()[itemIndex] is MenuListItem item2)
                    {
                        opacity = (((float)item2.ListIndex + 1) / 10f) - 0.1f;
                    }
                    else if (_menu.GetMenuItems()[itemIndex + 1] is MenuListItem item1)
                    {
                        opacity = (((float)item1.ListIndex + 1) / 10f) - 0.1f;
                    }
                    else if (_menu.GetMenuItems()[itemIndex - 1] is MenuListItem item)
                    {
                        opacity = (((float)item.ListIndex + 1) / 10f) - 0.1f;
                    }
                    else
                    {
                        opacity = 1f;
                    }

                    switch (itemIndex)
                    {
                        case 4: // blemishes
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 0, selection, opacity);
                            currentCharacter.PedAppearance.blemishesStyle = selection;
                            currentCharacter.PedAppearance.blemishesOpacity = opacity;
                            break;
                        case 6: // beards
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 1, selection, opacity);
                            currentCharacter.PedAppearance.beardStyle = selection;
                            currentCharacter.PedAppearance.beardOpacity = opacity;
                            break;
                        case 9: // eyebrows
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 2, selection, opacity);
                            currentCharacter.PedAppearance.eyebrowsStyle = selection;
                            currentCharacter.PedAppearance.eyebrowsOpacity = opacity;
                            break;
                        case 12: // ageing
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 3, selection, opacity);
                            currentCharacter.PedAppearance.ageingStyle = selection;
                            currentCharacter.PedAppearance.ageingOpacity = opacity;
                            break;
                        case 14: // makeup
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 4, selection, opacity);
                            currentCharacter.PedAppearance.makeupStyle = selection;
                            currentCharacter.PedAppearance.makeupOpacity = opacity;
                            break;
                        case 17: // blush style
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 5, selection, opacity);
                            currentCharacter.PedAppearance.blushStyle = selection;
                            currentCharacter.PedAppearance.blushOpacity = opacity;
                            break;
                        case 20: // complexion
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 6, selection, opacity);
                            currentCharacter.PedAppearance.complexionStyle = selection;
                            currentCharacter.PedAppearance.complexionOpacity = opacity;
                            break;
                        case 22: // sun damage
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 7, selection, opacity);
                            currentCharacter.PedAppearance.sunDamageStyle = selection;
                            currentCharacter.PedAppearance.sunDamageOpacity = opacity;
                            break;
                        case 24: // lipstick
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 8, selection, opacity);
                            currentCharacter.PedAppearance.lipstickStyle = selection;
                            currentCharacter.PedAppearance.lipstickOpacity = opacity;
                            break;
                        case 27: // moles and freckles
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 9, selection, opacity);
                            currentCharacter.PedAppearance.molesFrecklesStyle = selection;
                            currentCharacter.PedAppearance.molesFrecklesOpacity = opacity;
                            break;
                        case 29: // chest hair
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 10, selection, opacity);
                            currentCharacter.PedAppearance.chestHairStyle = selection;
                            currentCharacter.PedAppearance.chestHairOpacity = opacity;
                            break;
                        case 32: // body blemishes
                            SetPedHeadOverlay(Game.PlayerPed.Handle, 11, selection, opacity);
                            currentCharacter.PedAppearance.bodyBlemishesStyle = selection;
                            currentCharacter.PedAppearance.bodyBlemishesOpacity = opacity;
                            break;
                    }
                }
            };
            #endregion

            #region clothes
            clothesMenu.OnListIndexChange += (_menu, listItem, oldSelectionIndex, newSelectionIndex, realIndex) =>
            {
                var componentIndex = realIndex + 1;
                if (realIndex > 0)
                {
                    componentIndex += 1;
                }

                var textureIndex = GetPedTextureVariation(Game.PlayerPed.Handle, componentIndex);
                var newTextureIndex = 0;
                SetPedComponentVariation(Game.PlayerPed.Handle, componentIndex, newSelectionIndex, newTextureIndex, 0);
                currentCharacter.DrawableVariations.clothes ??= new Dictionary<int, KeyValuePair<int, int>>();

                var maxTextures = GetNumberOfPedTextureVariations(Game.PlayerPed.Handle, componentIndex, newSelectionIndex);

                currentCharacter.DrawableVariations.clothes[componentIndex] = new KeyValuePair<int, int>(newSelectionIndex, newTextureIndex);
                RecordEditedSlot(ClothingSlot.Component(componentIndex));
                listItem.Description = $"Select a drawable using the arrow keys and press ~o~enter~s~ to cycle through all available textures. Currently selected texture: #{newTextureIndex + 1} (of {maxTextures}).";
                RefreshCurrentCharacterSnapshot();
            };

            clothesMenu.OnListItemSelect += (sender, listItem, listIndex, realIndex) =>
            {
                var componentIndex = realIndex + 1; // skip face options as that fucks up with inheritance faces
                if (realIndex > 0) // skip hair features as that is done in the appeareance menu
                {
                    componentIndex += 1;
                }

                var textureIndex = GetPedTextureVariation(Game.PlayerPed.Handle, componentIndex);
                var newTextureIndex = GetNumberOfPedTextureVariations(Game.PlayerPed.Handle, componentIndex, listIndex) - 1 < textureIndex + 1 ? 0 : textureIndex + 1;
                SetPedComponentVariation(Game.PlayerPed.Handle, componentIndex, listIndex, newTextureIndex, 0);
                currentCharacter.DrawableVariations.clothes ??= new Dictionary<int, KeyValuePair<int, int>>();

                var maxTextures = GetNumberOfPedTextureVariations(Game.PlayerPed.Handle, componentIndex, listIndex);

                currentCharacter.DrawableVariations.clothes[componentIndex] = new KeyValuePair<int, int>(listIndex, newTextureIndex);
                RecordEditedSlot(ClothingSlot.Component(componentIndex));
                listItem.Description = $"Select a drawable using the arrow keys and press ~o~enter~s~ to cycle through all available textures. Currently selected texture: #{newTextureIndex + 1} (of {maxTextures}).";
                RefreshCurrentCharacterSnapshot();
            };
            #endregion

            #region props
            propsMenu.OnListIndexChange += (_menu, listItem, oldSelectionIndex, newSelectionIndex, realIndex) =>
            {
                var propIndex = realIndex;
                if (realIndex == 3)
                {
                    propIndex = 6;
                }
                if (realIndex == 4)
                {
                    propIndex = 7;
                }

                var textureIndex = 0;
                if (newSelectionIndex >= GetNumberOfPedPropDrawableVariations(Game.PlayerPed.Handle, propIndex))
                {
                    SetPedPropIndex(Game.PlayerPed.Handle, propIndex, -1, -1, false);
                    ClearPedProp(Game.PlayerPed.Handle, propIndex);
                    currentCharacter.PropVariations.props ??= new Dictionary<int, KeyValuePair<int, int>>();
                    currentCharacter.PropVariations.props[propIndex] = new KeyValuePair<int, int>(-1, -1);
                    listItem.Description = $"Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures.";
                }
                else
                {
                    SetPedPropIndex(Game.PlayerPed.Handle, propIndex, newSelectionIndex, textureIndex, true);
                    currentCharacter.PropVariations.props ??= new Dictionary<int, KeyValuePair<int, int>>();
                    currentCharacter.PropVariations.props[propIndex] = new KeyValuePair<int, int>(newSelectionIndex, textureIndex);
                    if (GetPedPropIndex(Game.PlayerPed.Handle, propIndex) == -1)
                    {
                        listItem.Description = $"Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures.";
                    }
                    else
                    {
                        var maxPropTextures = GetNumberOfPedPropTextureVariations(Game.PlayerPed.Handle, propIndex, newSelectionIndex);
                        listItem.Description = $"Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures. Currently selected texture: #{textureIndex + 1} (of {maxPropTextures}).";
                    }
                }
                RecordEditedSlot(ClothingSlot.PropSlot(propIndex));
                RefreshCurrentCharacterSnapshot();
            };

            propsMenu.OnListItemSelect += (sender, listItem, listIndex, realIndex) =>
            {
                var propIndex = realIndex;
                if (realIndex == 3)
                {
                    propIndex = 6;
                }
                if (realIndex == 4)
                {
                    propIndex = 7;
                }

                var textureIndex = GetPedPropTextureIndex(Game.PlayerPed.Handle, propIndex);
                var newTextureIndex = GetNumberOfPedPropTextureVariations(Game.PlayerPed.Handle, propIndex, listIndex) - 1 < textureIndex + 1 ? 0 : textureIndex + 1;
                if (textureIndex >= GetNumberOfPedPropDrawableVariations(Game.PlayerPed.Handle, propIndex))
                {
                    SetPedPropIndex(Game.PlayerPed.Handle, propIndex, -1, -1, false);
                    ClearPedProp(Game.PlayerPed.Handle, propIndex);
                    currentCharacter.PropVariations.props ??= new Dictionary<int, KeyValuePair<int, int>>();
                    currentCharacter.PropVariations.props[propIndex] = new KeyValuePair<int, int>(-1, -1);
                    listItem.Description = $"Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures.";
                }
                else
                {
                    SetPedPropIndex(Game.PlayerPed.Handle, propIndex, listIndex, newTextureIndex, true);
                    currentCharacter.PropVariations.props ??= new Dictionary<int, KeyValuePair<int, int>>();
                    currentCharacter.PropVariations.props[propIndex] = new KeyValuePair<int, int>(listIndex, newTextureIndex);
                    if (GetPedPropIndex(Game.PlayerPed.Handle, propIndex) == -1)
                    {
                        listItem.Description = $"Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures.";
                    }
                    else
                    {
                        var maxPropTextures = GetNumberOfPedPropTextureVariations(Game.PlayerPed.Handle, propIndex, listIndex);
                        listItem.Description = $"Select a prop using the arrow keys and press ~o~enter~s~ to cycle through all available textures. Currently selected texture: #{newTextureIndex + 1} (of {maxPropTextures}).";
                    }
                }
                //propsMenu.UpdateScaleform();
                RecordEditedSlot(ClothingSlot.PropSlot(propIndex));
                RefreshCurrentCharacterSnapshot();
            };
            #endregion

            #region face shape data
            /*
            Nose_Width  
            Nose_Peak_Hight  
            Nose_Peak_Lenght  
            Nose_Bone_High  
            Nose_Peak_Lowering  
            Nose_Bone_Twist  
            EyeBrown_High  
            EyeBrown_Forward  
            Cheeks_Bone_High  
            Cheeks_Bone_Width  
            Cheeks_Width  
            Eyes_Openning  
            Lips_Thickness  
            Jaw_Bone_Width 'Bone size to sides  
            Jaw_Bone_Back_Lenght 'Bone size to back  
            Chimp_Bone_Lowering 'Go Down  
            Chimp_Bone_Lenght 'Go forward  
            Chimp_Bone_Width  
            Chimp_Hole  
            Neck_Thikness  
            */

            var faceFeaturesNamesList = new string[20]
            {
                "Nose Width",               // 0
                "Noes Peak Height",         // 1
                "Nose Peak Length",         // 2
                "Nose Bone Height",         // 3
                "Nose Peak Lowering",       // 4
                "Nose Bone Twist",          // 5
                "Eyebrows Height",          // 6
                "Eyebrows Depth",           // 7
                "Cheekbones Height",        // 8
                "Cheekbones Width",         // 9
                "Cheeks Width",             // 10
                "Eyes Opening",             // 11
                "Lips Thickness",           // 12
                "Jaw Bone Width",           // 13
                "Jaw Bone Depth/Length",    // 14
                "Chin Height",              // 15
                "Chin Depth/Length",        // 16
                "Chin Width",               // 17
                "Chin Hole Size",           // 18
                "Neck Thickness"            // 19
            };

            for (var i = 0; i < 20; i++)
            {
                var faceFeature = new MenuSliderItem(faceFeaturesNamesList[i], $"Set the {faceFeaturesNamesList[i]} face feature value.", 0, 20, 10, true);
                faceShapeMenu.AddMenuItem(faceFeature);
            }

            faceShapeMenu.OnSliderPositionChange += (sender, sliderItem, oldPosition, newPosition, itemIndex) =>
            {
                currentCharacter.FaceShapeFeatures.features ??= new Dictionary<int, float>();
                var value = faceFeaturesValuesList[newPosition];
                currentCharacter.FaceShapeFeatures.features[itemIndex] = value;
                SetPedFaceFeature(Game.PlayerPed.Handle, itemIndex, value);
            };

            #endregion

            #region tattoos
            void CreateListsIfNull()
            {
                currentCharacter.PedTatttoos.HeadTattoos ??= new List<KeyValuePair<string, string>>();
                currentCharacter.PedTatttoos.TorsoTattoos ??= new List<KeyValuePair<string, string>>();
                currentCharacter.PedTatttoos.LeftArmTattoos ??= new List<KeyValuePair<string, string>>();
                currentCharacter.PedTatttoos.RightArmTattoos ??= new List<KeyValuePair<string, string>>();
                currentCharacter.PedTatttoos.LeftLegTattoos ??= new List<KeyValuePair<string, string>>();
                currentCharacter.PedTatttoos.RightLegTattoos ??= new List<KeyValuePair<string, string>>();
                currentCharacter.PedTatttoos.BadgeTattoos ??= new List<KeyValuePair<string, string>>();
                // Tattoos saved by hash that vMenu's list now names show as worn (TattooCatalog.UseNames).
                TattoosData.UseCatalogNames(currentCharacter.PedTatttoos.HeadTattoos);
                TattoosData.UseCatalogNames(currentCharacter.PedTatttoos.TorsoTattoos);
                TattoosData.UseCatalogNames(currentCharacter.PedTatttoos.LeftArmTattoos);
                TattoosData.UseCatalogNames(currentCharacter.PedTatttoos.RightArmTattoos);
                TattoosData.UseCatalogNames(currentCharacter.PedTatttoos.LeftLegTattoos);
                TattoosData.UseCatalogNames(currentCharacter.PedTatttoos.RightLegTattoos);
                TattoosData.UseCatalogNames(currentCharacter.PedTatttoos.BadgeTattoos);
            }

            void ApplySavedTattoos()
            {
                // remove all decorations, and then manually re-add them all. what a retarded way of doing this R*....
                ClearPedDecorations(Game.PlayerPed.Handle);

                foreach (var tattoo in currentCharacter.PedTatttoos.HeadTattoos)
                {
                    SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
                }
                foreach (var tattoo in currentCharacter.PedTatttoos.TorsoTattoos)
                {
                    SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
                }
                foreach (var tattoo in currentCharacter.PedTatttoos.LeftArmTattoos)
                {
                    SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
                }
                foreach (var tattoo in currentCharacter.PedTatttoos.RightArmTattoos)
                {
                    SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
                }
                foreach (var tattoo in currentCharacter.PedTatttoos.LeftLegTattoos)
                {
                    SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
                }
                foreach (var tattoo in currentCharacter.PedTatttoos.RightLegTattoos)
                {
                    SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
                }
                foreach (var tattoo in currentCharacter.PedTatttoos.BadgeTattoos)
                {
                    SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
                }

                if (!string.IsNullOrEmpty(currentCharacter.PedAppearance.HairOverlay.Key) && !string.IsNullOrEmpty(currentCharacter.PedAppearance.HairOverlay.Value))
                {
                    // reset hair value
                    SetPedDecoration(Game.PlayerPed.Handle, (uint)GetHashKey(currentCharacter.PedAppearance.HairOverlay.Key), (uint)GetHashKey(currentCharacter.PedAppearance.HairOverlay.Value));
                }
            }

            tattoosMenu.OnIndexChange += (sender, oldItem, newItem, oldIndex, newIndex) =>
            {
                CreateListsIfNull();
                ApplySavedTattoos();
            };

            #region tattoos menu list select events
            tattoosMenu.OnListIndexChange += (sender, item, oldIndex, tattooIndex, menuIndex) =>
            {
                CreateListsIfNull();
                ApplySavedTattoos();
                if (menuIndex == 0) // head
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.HEAD.ElementAt(tattooIndex) : FemaleTattoosCollection.HEAD.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (!currentCharacter.PedTatttoos.HeadTattoos.Contains(tat))
                    {
                        SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tat.Key), TattoosData.HashOf(tat.Value));
                    }
                }
                else if (menuIndex == 1) // torso
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.TORSO.ElementAt(tattooIndex) : FemaleTattoosCollection.TORSO.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (!currentCharacter.PedTatttoos.TorsoTattoos.Contains(tat))
                    {
                        SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tat.Key), TattoosData.HashOf(tat.Value));
                    }
                }
                else if (menuIndex == 2) // left arm
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.LEFT_ARM.ElementAt(tattooIndex) : FemaleTattoosCollection.LEFT_ARM.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (!currentCharacter.PedTatttoos.LeftArmTattoos.Contains(tat))
                    {
                        SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tat.Key), TattoosData.HashOf(tat.Value));
                    }
                }
                else if (menuIndex == 3) // right arm
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.RIGHT_ARM.ElementAt(tattooIndex) : FemaleTattoosCollection.RIGHT_ARM.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (!currentCharacter.PedTatttoos.RightArmTattoos.Contains(tat))
                    {
                        SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tat.Key), TattoosData.HashOf(tat.Value));
                    }
                }
                else if (menuIndex == 4) // left leg
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.LEFT_LEG.ElementAt(tattooIndex) : FemaleTattoosCollection.LEFT_LEG.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (!currentCharacter.PedTatttoos.LeftLegTattoos.Contains(tat))
                    {
                        SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tat.Key), TattoosData.HashOf(tat.Value));
                    }
                }
                else if (menuIndex == 5) // right leg
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.RIGHT_LEG.ElementAt(tattooIndex) : FemaleTattoosCollection.RIGHT_LEG.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (!currentCharacter.PedTatttoos.RightLegTattoos.Contains(tat))
                    {
                        SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tat.Key), TattoosData.HashOf(tat.Value));
                    }
                }
                else if (menuIndex == 6) // badges
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.BADGES.ElementAt(tattooIndex) : FemaleTattoosCollection.BADGES.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (!currentCharacter.PedTatttoos.BadgeTattoos.Contains(tat))
                    {
                        SetPedDecoration(Game.PlayerPed.Handle, TattoosData.HashOf(tat.Key), TattoosData.HashOf(tat.Value));
                    }
                }
            };

            tattoosMenu.OnListItemSelect += (sender, item, tattooIndex, menuIndex) =>
            {
                CreateListsIfNull();

                if (menuIndex == 0) // head
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.HEAD.ElementAt(tattooIndex) : FemaleTattoosCollection.HEAD.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (currentCharacter.PedTatttoos.HeadTattoos.Contains(tat))
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~r~removed~s~.");
                        currentCharacter.PedTatttoos.HeadTattoos.Remove(tat);
                    }
                    else
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~g~added~s~.");
                        currentCharacter.PedTatttoos.HeadTattoos.Add(tat);
                    }
                }
                else if (menuIndex == 1) // torso
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.TORSO.ElementAt(tattooIndex) : FemaleTattoosCollection.TORSO.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (currentCharacter.PedTatttoos.TorsoTattoos.Contains(tat))
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~r~removed~s~.");
                        currentCharacter.PedTatttoos.TorsoTattoos.Remove(tat);
                    }
                    else
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~g~added~s~.");
                        currentCharacter.PedTatttoos.TorsoTattoos.Add(tat);
                    }
                }
                else if (menuIndex == 2) // left arm
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.LEFT_ARM.ElementAt(tattooIndex) : FemaleTattoosCollection.LEFT_ARM.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (currentCharacter.PedTatttoos.LeftArmTattoos.Contains(tat))
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~r~removed~s~.");
                        currentCharacter.PedTatttoos.LeftArmTattoos.Remove(tat);
                    }
                    else
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~g~added~s~.");
                        currentCharacter.PedTatttoos.LeftArmTattoos.Add(tat);
                    }
                }
                else if (menuIndex == 3) // right arm
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.RIGHT_ARM.ElementAt(tattooIndex) : FemaleTattoosCollection.RIGHT_ARM.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (currentCharacter.PedTatttoos.RightArmTattoos.Contains(tat))
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~r~removed~s~.");
                        currentCharacter.PedTatttoos.RightArmTattoos.Remove(tat);
                    }
                    else
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~g~added~s~.");
                        currentCharacter.PedTatttoos.RightArmTattoos.Add(tat);
                    }
                }
                else if (menuIndex == 4) // left leg
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.LEFT_LEG.ElementAt(tattooIndex) : FemaleTattoosCollection.LEFT_LEG.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (currentCharacter.PedTatttoos.LeftLegTattoos.Contains(tat))
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~r~removed~s~.");
                        currentCharacter.PedTatttoos.LeftLegTattoos.Remove(tat);
                    }
                    else
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~g~added~s~.");
                        currentCharacter.PedTatttoos.LeftLegTattoos.Add(tat);
                    }
                }
                else if (menuIndex == 5) // right leg
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.RIGHT_LEG.ElementAt(tattooIndex) : FemaleTattoosCollection.RIGHT_LEG.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (currentCharacter.PedTatttoos.RightLegTattoos.Contains(tat))
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~r~removed~s~.");
                        currentCharacter.PedTatttoos.RightLegTattoos.Remove(tat);
                    }
                    else
                    {
                        Subtitle.Custom($"Tattoo #{tattooIndex + 1} has been ~g~added~s~.");
                        currentCharacter.PedTatttoos.RightLegTattoos.Add(tat);
                    }
                }
                else if (menuIndex == 6) // badges
                {
                    var Tattoo = currentCharacter.IsMale ? MaleTattoosCollection.BADGES.ElementAt(tattooIndex) : FemaleTattoosCollection.BADGES.ElementAt(tattooIndex);
                    var tat = new KeyValuePair<string, string>(Tattoo.collectionName, Tattoo.name);
                    if (currentCharacter.PedTatttoos.BadgeTattoos.Contains(tat))
                    {
                        Subtitle.Custom($"Badge #{tattooIndex + 1} has been ~r~removed~s~.");
                        currentCharacter.PedTatttoos.BadgeTattoos.Remove(tat);
                    }
                    else
                    {
                        Subtitle.Custom($"Badge #{tattooIndex + 1} has been ~g~added~s~.");
                        currentCharacter.PedTatttoos.BadgeTattoos.Add(tat);
                    }
                }

                ApplySavedTattoos();

            };

            // eventhandler for when a tattoo is selected.
            tattoosMenu.OnItemSelect += (sender, item, index) =>
            {
                Notify.Success("All tattoos have been removed.");
                currentCharacter.PedTatttoos.HeadTattoos.Clear();
                currentCharacter.PedTatttoos.TorsoTattoos.Clear();
                currentCharacter.PedTatttoos.LeftArmTattoos.Clear();
                currentCharacter.PedTatttoos.RightArmTattoos.Clear();
                currentCharacter.PedTatttoos.LeftLegTattoos.Clear();
                currentCharacter.PedTatttoos.RightLegTattoos.Clear();
                currentCharacter.PedTatttoos.BadgeTattoos.Clear();
                ClearPedDecorations(Game.PlayerPed.Handle);
            };

            #endregion
            #endregion


            // handle list changes in the character creator menu.
            createCharacterMenu.OnListIndexChange += (sender, item, oldListIndex, newListIndex, itemIndex) =>
            {
                if (item == faceExpressionList)
                {
                    currentCharacter.FacialExpression = facial_expressions[newListIndex];
                    SetFacialIdleAnimOverride(Game.PlayerPed.Handle, currentCharacter.FacialExpression ?? facial_expressions[0], null);
                }
                else if (item == categoryBtn)
                {
                    List<string> categoryNames = categoryBtn.ItemData.Item1;
                    List<MenuItem.Icon> categoryIcons = categoryBtn.ItemData.Item2;
                    currentCharacter.Category = categoryNames[newListIndex];
                    categoryBtn.RightIcon = categoryIcons[newListIndex];
                }
            };

            // handle button presses for the createCharacter menu.
            createCharacterMenu.OnItemSelect += async (sender, item, index) =>
            {
                if (item == randomizeButton)
                {
                    faceSelection.Randomize(_random);

                    if (currentCharacter.FaceShapeFeatures.features == null)
                    {
                        currentCharacter.FaceShapeFeatures.features = [];
                    }

                    for (int i = 0; i < 20; i++)
                    {
                        var value = faceFeaturesValuesList[_random.Next(5, 15)];
                        SetPedFaceFeature(Game.PlayerPed.Handle, i, value);
                        currentCharacter.FaceShapeFeatures.features[i] = value;
                    }

                    int bodyHair = _random.Next(31);

                    ChangePlayerHair(_random.Next(0, GetNumberOfPedDrawableVariations(Game.PlayerPed.Handle, 2)));
                    ChangePlayerHairColor(bodyHair, _random.Next(31));
                    ChangePlayerEyeColor(_random.Next(0, 9));

                    for (int i = 0; i < 12; i++)
                    {
                        int value;
                        int colorIndex = 0;
                        bool colorRequired = false;

                        int color = i == 1 || i == 2 || i == 10 ? bodyHair : _random.Next(17);
                        float opacity = (float)_random.NextDouble();

                        switch (i)
                        {
                            case 0:
                                value = _random.Next(blemishesStyleList.Count);

                                currentCharacter.PedAppearance.blemishesStyle = value;
                                currentCharacter.PedAppearance.blemishesOpacity = opacity;
                                break;

                            case 1:
                                if (!currentCharacter.IsMale)
                                {
                                    appearanceValues[i] = new Tuple<int, int, float>(0, 0, 0f);
                                    continue;
                                }

                                value = _random.Next(beardStylesList.Count);
                                colorRequired = true;
                                colorIndex = 1;

                                currentCharacter.PedAppearance.beardStyle = value;
                                currentCharacter.PedAppearance.beardColor = color;
                                currentCharacter.PedAppearance.beardOpacity = opacity;
                                break;

                            case 2:
                                value = _random.Next(eyebrowsStyleList.Count);
                                colorRequired = true;
                                colorIndex = 1;

                                currentCharacter.PedAppearance.eyebrowsStyle = value;
                                currentCharacter.PedAppearance.eyebrowsColor = color;
                                currentCharacter.PedAppearance.eyebrowsOpacity = opacity;
                                break;

                            case 3:
                                value = _random.Next(ageingStyleList.Count);

                                currentCharacter.PedAppearance.ageingStyle = value;
                                currentCharacter.PedAppearance.ageingOpacity = opacity;
                                break;

                            case 8:
                                if (currentCharacter.IsMale)
                                {
                                    appearanceValues[i] = new Tuple<int, int, float>(0, 0, 0f);
                                    continue;
                                }

                                value = _random.Next(6);
                                colorRequired = true;
                                colorIndex = 2;

                                currentCharacter.PedAppearance.lipstickStyle = value;
                                currentCharacter.PedAppearance.lipstickColor = color;
                                currentCharacter.PedAppearance.lipstickOpacity = opacity;
                                break;

                            case 9:
                                value = _random.Next(molesFrecklesStyleList.Count);

                                currentCharacter.PedAppearance.molesFrecklesStyle = value;
                                currentCharacter.PedAppearance.molesFrecklesOpacity = opacity;
                                break;

                            case 10:
                                if (!currentCharacter.IsMale)
                                {
                                    appearanceValues[i] = new Tuple<int, int, float>(0, 0, 0f);
                                    continue;
                                }

                                value = _random.Next(8);
                                colorRequired = true;
                                colorIndex = 1;

                                currentCharacter.PedAppearance.chestHairStyle = value;
                                currentCharacter.PedAppearance.chestHairColor = color;
                                currentCharacter.PedAppearance.chestHairOpacity = opacity;
                                break;

                            case 11:
                                value = _random.Next(bodyBlemishesList.Count);

                                currentCharacter.PedAppearance.bodyBlemishesStyle = value;
                                currentCharacter.PedAppearance.bodyBlemishesOpacity = opacity;
                                break;

                            default:
                                appearanceValues[i] = new Tuple<int, int, float>(0, 0, 0);
                                continue;
                        }

                        appearanceValues[i] = new Tuple<int, int, float>(value, color, opacity);
                        SetPedHeadOverlay(Game.PlayerPed.Handle, i, appearanceValues[i].Item1, appearanceValues[i].Item3);

                        if (colorRequired)
                        {
                            SetPedHeadOverlayColor(Game.PlayerPed.Handle, i, colorIndex, appearanceValues[i].Item2, appearanceValues[i].Item2);
                        }
                    }

                    _facialExpressionSelection = _random.Next(facial_expressions.Count);

                    SetFacialIdleAnimOverride(Game.PlayerPed.Handle, facial_expressions[_facialExpressionSelection], null);

                    currentCharacter.FacialExpression = facial_expressions[_facialExpressionSelection];

                    faceExpressionList.ListIndex = _facialExpressionSelection;

                    SetPlayerClothing();
                }
                else if (item == saveButton) // save ped
                {
                    if (await SavePed())
                    {
                        // The dressing room's last step (a new player's first character).
                        DressingRoomGuide.CharacterSaved();

                        while (!MenuController.IsAnyMenuOpen())
                        {
                            await BaseScript.Delay(0);
                        }

                        while (IsControlPressed(2, 201) || IsControlPressed(2, 217) || IsDisabledControlPressed(2, 201) || IsDisabledControlPressed(2, 217))
                        {
                            await BaseScript.Delay(0);
                        }

                        await BaseScript.Delay(100);

                        createCharacterMenu.GoBack();
                    }
                }
                else if (item == loadOutfitCodeButton)
                {
                    if (currentCharacter.SaveName == null)
                    {
                        Notify.Error("You must save the current character before you can load a code for it.");
                    }
                    else
                    {
                        bool success = await LoadSharedOutfit(currentCharacter.SaveName);
                        if (success)
                        {
                            MenuController.CloseAllMenus();
                            UpdateSavedPedsMenu();
                            savedCharactersMenu.OpenMenu();
                        }
                    }
                }
                else if (item == generateOutfitCodeButton)
                {
                    if (currentCharacter.SaveName == null)
                    {
                        Notify.Error("You must save the current character before you can generate a code for it.");
                    }
                    else
                    {
                        TriggerEvent("vMenu:Outfits:GenerateCode", currentCharacter.SaveName);
                    }
                }
                else if (item == exitNoSave) // exit without saving
                {
                    var confirm = false;
                    AddTextEntry("vmenu_warning_message_first_line", "Are you sure you want to exit the character creator?");
                    AddTextEntry("vmenu_warning_message_second_line", "You will lose all (unsaved) customization!");
                    createCharacterMenu.CloseMenu();

                    // wait for confirmation or cancel input.
                    while (true)
                    {
                        await BaseScript.Delay(0);
                        var unk = 1;
                        var unk2 = 1;
                        SetWarningMessage("vmenu_warning_message_first_line", 20, "vmenu_warning_message_second_line", true, 0, ref unk, ref unk2, true, 0);
                        if (IsControlJustPressed(2, 201) || IsControlJustPressed(2, 217)) // continue/accept
                        {
                            confirm = true;
                            break;
                        }
                        else if (IsControlJustPressed(2, 202)) // cancel
                        {
                            break;
                        }
                    }

                    // if confirmed to discard changes quit the editor.
                    if (confirm)
                    {
                        while (IsControlPressed(2, 201) || IsControlPressed(2, 217) || IsDisabledControlPressed(2, 201) || IsDisabledControlPressed(2, 217))
                        {
                            await BaseScript.Delay(0);
                        }

                        await BaseScript.Delay(100);
                        menu.OpenMenu();
                    }
                    else // otherwise cancel and go back to the editor.
                    {
                        createCharacterMenu.OpenMenu();
                    }
                }
                else if (item == inheritanceButton) // update the inheritance menu anytime it's opened to prevent some weird glitch where old data is used.
                {
                    faceSelection.Refresh();
                }
                else if (item == faceButton)
                {
                    foreach (var slider in faceShapeMenu.GetMenuItems().OfType<MenuSliderItem>())
                    {
                        slider.Position = FaceFeatureValues.GetSliderPosition(currentCharacter.FaceShapeFeatures.features, slider.Index);
                    }
                    faceShapeMenu.RefreshIndex();
                }
                else if (item == appearanceButton)
                {
                    List<MenuListItem> menuListItems = [.. appearanceMenu.GetMenuItems().OfType<MenuListItem>()];

                    menuListItems.First(i => i.Text == "Hair Style").ListIndex = _hairSelection;
                    menuListItems.First(i => i.Text == "Hair Color").ListIndex = _hairColorSelection;
                    menuListItems.First(i => i.Text == "Hair Highlight Color").ListIndex = _hairHighlightColorSelection;

                    menuListItems.First(i => i.Text == "Blemishes Style").ListIndex = appearanceValues[0].Item1;
                    menuListItems.First(i => i.Text == "Blemishes Opacity").ListIndex = (int)(appearanceValues[0].Item3 * 10);

                    menuListItems.First(i => i.Text == "Beard Style").ListIndex = appearanceValues[1].Item1;
                    menuListItems.First(i => i.Text == "Beard Opacity").ListIndex = (int)(appearanceValues[1].Item3 * 10);
                    menuListItems.First(i => i.Text == "Beard Color").ListIndex = appearanceValues[1].Item2;

                    menuListItems.First(i => i.Text == "Eyebrows Style").ListIndex = appearanceValues[2].Item1;
                    menuListItems.First(i => i.Text == "Eyebrows Opacity").ListIndex = (int)(appearanceValues[2].Item3 * 10);
                    menuListItems.First(i => i.Text == "Eyebrows Color").ListIndex = appearanceValues[2].Item2;

                    menuListItems.First(i => i.Text == "Ageing Style").ListIndex = appearanceValues[3].Item1;
                    menuListItems.First(i => i.Text == "Ageing Opacity").ListIndex = (int)(appearanceValues[3].Item3 * 10);

                    menuListItems.First(i => i.Text == "Makeup Style").ListIndex = appearanceValues[4].Item1;
                    menuListItems.First(i => i.Text == "Makeup Opacity").ListIndex = (int)(appearanceValues[4].Item3 * 10);
                    menuListItems.First(i => i.Text == "Makeup Color").ListIndex = appearanceValues[4].Item2;

                    menuListItems.First(i => i.Text == "Blush Style").ListIndex = appearanceValues[5].Item1;
                    menuListItems.First(i => i.Text == "Blush Opacity").ListIndex = (int)(appearanceValues[5].Item3 * 10);
                    menuListItems.First(i => i.Text == "Blush Color").ListIndex = appearanceValues[5].Item2;

                    menuListItems.First(i => i.Text == "Complexion Style").ListIndex = appearanceValues[6].Item1;
                    menuListItems.First(i => i.Text == "Complexion Opacity").ListIndex = (int)(appearanceValues[6].Item3 * 10);

                    menuListItems.First(i => i.Text == "Sun Damage Style").ListIndex = appearanceValues[7].Item1;
                    menuListItems.First(i => i.Text == "Sun Damage Opacity").ListIndex = (int)(appearanceValues[7].Item3 * 10);

                    menuListItems.First(i => i.Text == "Lipstick Style").ListIndex = appearanceValues[8].Item1;
                    menuListItems.First(i => i.Text == "Lipstick Opacity").ListIndex = (int)(appearanceValues[8].Item3 * 10);
                    menuListItems.First(i => i.Text == "Lipstick Color").ListIndex = appearanceValues[8].Item2;

                    menuListItems.First(i => i.Text == "Moles and Freckles Style").ListIndex = appearanceValues[9].Item1;
                    menuListItems.First(i => i.Text == "Moles and Freckles Opacity").ListIndex = (int)(appearanceValues[9].Item3 * 10);

                    menuListItems.First(i => i.Text == "Chest Hair Style").ListIndex = appearanceValues[10].Item1;
                    menuListItems.First(i => i.Text == "Chest Hair Opacity").ListIndex = (int)(appearanceValues[10].Item3 * 10);
                    menuListItems.First(i => i.Text == "Chest Hair Color").ListIndex = appearanceValues[10].Item2;

                    menuListItems.First(i => i.Text == "Body Blemishes Style").ListIndex = appearanceValues[11].Item1;
                    menuListItems.First(i => i.Text == "Body Blemishes Opacity").ListIndex = (int)(appearanceValues[11].Item3 * 10);

                    menuListItems.First(i => i.Text == "Eye Colors").ListIndex = _eyeColorSelection;

                    appearanceMenu.RefreshIndex();

                }
            };

            // eventhandler for whenever a menu item is selected in the main mp characters menu.
            menu.OnItemSelect += async (sender, item, index) =>
            {
                if (item == createMaleBtn)
                {
                    var model = (uint)GetHashKey("mp_m_freemode_01");

                    if (!await LoadModelAsync(model))
                    {
                        Notify.Error(CommonErrors.CouldNotLoad, placeholderValue: "the character model");
                        return;
                    }

                    var maxHealth = Game.PlayerPed.MaxHealth;
                    var maxArmour = Game.Player.MaxArmor;
                    var health = Game.PlayerPed.Health;
                    var armour = Game.PlayerPed.Armor;

                    SaveWeaponLoadout("vmenu_temp_weapons_loadout_before_respawn");
                    SetPlayerModel(Game.Player.Handle, model);
                    // Restore vitals before awaiting: the new ped spawns at full health.
                    Game.Player.MaxArmor = maxArmour;
                    Game.PlayerPed.MaxHealth = maxHealth;
                    Game.PlayerPed.Health = health;
                    Game.PlayerPed.Armor = armour;
                    await SpawnWeaponLoadoutAsync("vmenu_temp_weapons_loadout_before_respawn", false, true, true);

                    ClearPedDecorations(Game.PlayerPed.Handle);
                    ClearPedFacialDecorations(Game.PlayerPed.Handle);
                    SetPedDefaultComponentVariation(Game.PlayerPed.Handle);
                    ClearAllPedProps(Game.PlayerPed.Handle);
                    DefaultPlayerColors();

                    MakeCreateCharacterMenu(male: true);
                }
                else if (item == createFemaleBtn)
                {
                    var model = (uint)GetHashKey("mp_f_freemode_01");

                    if (!await LoadModelAsync(model))
                    {
                        Notify.Error(CommonErrors.CouldNotLoad, placeholderValue: "the character model");
                        return;
                    }

                    var maxHealth = Game.PlayerPed.MaxHealth;
                    var maxArmour = Game.Player.MaxArmor;
                    var health = Game.PlayerPed.Health;
                    var armour = Game.PlayerPed.Armor;

                    SaveWeaponLoadout("vmenu_temp_weapons_loadout_before_respawn");
                    SetPlayerModel(Game.Player.Handle, model);
                    // Restore vitals before awaiting: the new ped spawns at full health.
                    Game.Player.MaxArmor = maxArmour;
                    Game.PlayerPed.MaxHealth = maxHealth;
                    Game.PlayerPed.Health = health;
                    Game.PlayerPed.Armor = armour;
                    await SpawnWeaponLoadoutAsync("vmenu_temp_weapons_loadout_before_respawn", false, true, true);

                    ClearPedDecorations(Game.PlayerPed.Handle);
                    ClearPedFacialDecorations(Game.PlayerPed.Handle);
                    SetPedDefaultComponentVariation(Game.PlayerPed.Handle);
                    ClearAllPedProps(Game.PlayerPed.Handle);
                    DefaultPlayerColors();

                    MakeCreateCharacterMenu(male: false);
                }
                else if (item == savedCharacters)
                {
                    UpdateSavedPedsMenu();
                }
            };
        }

        /// <summary>
        /// Spawns this saved ped.
        /// </summary>
        /// <param name="name"></param>
        internal async Task SpawnThisCharacter(string name, bool restoreWeapons)
        {
            currentCharacter = StorageManager.GetSavedMpCharacterData(name);
            await SpawnSavedPed(restoreWeapons);
        }

        internal async Task<bool> RestorePreferredMpAppearance(bool afterDeath)
        {
            var defaultChar = GetResourceKvpString("vmenu_default_character");
            if (MainMenu.MiscSettingsMenu != null
                && MainMenu.MiscSettingsMenu.MiscRespawnDefaultCharacter
                && !string.IsNullOrEmpty(defaultChar))
            {
                var character = StorageManager.GetSavedMpCharacterData(defaultChar);
                if (character.Version >= 1)
                {
                    await SpawnCharacter(character, false);
                    return true;
                }
            }

            var snapshot = GetLastMpCharacterSnapshot();
            if (snapshot.Version >= 1)
            {
                await SpawnCharacter(snapshot, restoreWeapons: false);
                return true;
            }

            var lastChar = NormalizeMpCharacterKey(GetResourceKvpString("vmenu_last_character"));
            if (!string.IsNullOrEmpty(lastChar))
            {
                var character = StorageManager.GetSavedMpCharacterData(lastChar);
                if (character.Version >= 1)
                {
                    await SpawnCharacter(character, false);
                    return true;
                }
            }

            return false;
        }

        internal bool CaptureCurrentMpAppearanceSnapshot()
        {
            if (!IsCurrentPedMpFreemode())
            {
                return false;
            }

            var lastChar = NormalizeMpCharacterKey(GetResourceKvpString("vmenu_last_character"));
            if (string.IsNullOrEmpty(lastChar))
            {
                return false;
            }

            var character = StorageManager.GetSavedMpCharacterData(lastChar);
            if (character.Version < 1)
            {
                return false;
            }

            if (character.ModelHash != (uint)GetEntityModel(Game.PlayerPed.Handle))
            {
                return false;
            }

            // Only the current look: pieces that couldn't be matched keep their saved entry.
            var otherCharacter = string.IsNullOrEmpty(currentCharacter.SaveName) || GetMpCharacterKey(currentCharacter.SaveName) != lastChar;
            if (otherCharacter)
            {
                unresolvedSlots.Clear(); // they belong to another character
            }
            character = ReplacePedDataClothing(character, keepUnresolved: true);
            return SaveMpCharacterSnapshot(character);
        }

        internal void ClearLastMpCharacterSnapshotIfMatches(string pedKey)
        {
            var snapshot = GetLastMpCharacterSnapshot();
            if (snapshot.Version >= 1 && GetMpCharacterKey(snapshot.SaveName) == pedKey)
            {
                DeleteResourceKvp(LastMpCharacterSnapshotKey);
            }
        }

        private void ClearMpCharacterReferences(string pedKey)
        {
            if (GetResourceKvpString("vmenu_default_character") == pedKey)
            {
                DeleteResourceKvp("vmenu_default_character");
            }

            if (GetResourceKvpString("vmenu_last_character") == pedKey)
            {
                DeleteResourceKvp("vmenu_last_character");
            }

            ClearLastMpCharacterSnapshotIfMatches(pedKey);

            // The deleted character's own outfits and hair & makeup styles go with it.
            CharacterWardrobe.DeleteAll(pedKey, KvpKeys, DeleteResourceKvp);
        }

        private void RenameMpCharacterReferences(string oldPedKey, string newPedKey)
        {
            // The character's own outfits and hair & makeup styles move to the new name.
            CharacterWardrobe.Transfer(oldPedKey, newPedKey, keepSource: false, KvpKeys, GetResourceKvpString, SetResourceKvp, DeleteResourceKvp);

            if (GetResourceKvpString("vmenu_default_character") == oldPedKey)
            {
                SetResourceKvp("vmenu_default_character", newPedKey);
            }

            if (GetResourceKvpString("vmenu_last_character") == oldPedKey)
            {
                SetResourceKvp("vmenu_last_character", newPedKey);
            }

            var snapshot = GetLastMpCharacterSnapshot();
            if (snapshot.Version >= 1 && GetMpCharacterKey(snapshot.SaveName) == oldPedKey)
            {
                snapshot.SaveName = newPedKey;
                SaveMpCharacterSnapshot(snapshot);
            }
        }

        /// <summary>
        /// After a save in the dressing room: the character just saved becomes the one the next join
        /// restores (RestorePreferredMpAppearance), as spawning it from Saved Characters would make it.
        /// A new character is otherwise only worn until the player leaves.
        /// </summary>
        internal void RememberCurrentCharacter()
        {
            if (currentCharacter.Version < 1 || string.IsNullOrEmpty(currentCharacter.SaveName))
            {
                return;
            }
            SetResourceKvp("vmenu_last_character", GetMpCharacterKey(currentCharacter.SaveName));
            SaveMpCharacterSnapshot(currentCharacter);
        }

        private void RefreshCurrentCharacterSnapshot()
        {
            if (currentCharacter.Version < 1 || string.IsNullOrEmpty(currentCharacter.SaveName))
            {
                return;
            }

            if (GetResourceKvpString("vmenu_last_character") != GetMpCharacterKey(currentCharacter.SaveName))
            {
                return;
            }

            SaveMpCharacterSnapshot(currentCharacter);
        }

        /// <summary>
        /// Spawns the ped from the data inside <see cref="currentCharacter"/>.
        /// Character data MUST be set BEFORE calling this function.
        /// </summary>
        /// <returns></returns>
        private async Task SpawnSavedPed(bool restoreWeapons)
        {
            if (currentCharacter.Version < 1)
            {
                return;
            }
            if (IsModelInCdimage(currentCharacter.ModelHash))
            {
                if (!await LoadModelAsync(currentCharacter.ModelHash))
                {
                    Notify.Error(CommonErrors.CouldNotLoad, placeholderValue: "the character model");
                    return;
                }
                var maxHealth = Game.PlayerPed.MaxHealth;
                var maxArmour = Game.Player.MaxArmor;
                var health = Game.PlayerPed.Health;
                var armour = Game.PlayerPed.Armor;

                TriggerEvent("vMenu:PedChange:Before");
                await BaseScript.Delay(0); // yield so external Lua handlers can save addon weapons
                SaveWeaponLoadout("vmenu_temp_weapons_loadout_before_respawn");
                SetPlayerModel(Game.Player.Handle, currentCharacter.ModelHash);
                // Restore vitals before awaiting: the new ped spawns at full health.
                Game.Player.MaxArmor = maxArmour;
                Game.PlayerPed.MaxHealth = maxHealth;
                Game.PlayerPed.Health = health;
                Game.PlayerPed.Armor = armour;
                await SpawnWeaponLoadoutAsync("vmenu_temp_weapons_loadout_before_respawn", false, true, true);
                TriggerEvent("vMenu:PedChange:After");

                ClearPedDecorations(Game.PlayerPed.Handle);
                ClearPedFacialDecorations(Game.PlayerPed.Handle);
                SetPedDefaultComponentVariation(Game.PlayerPed.Handle);
                SetPedHairColor(Game.PlayerPed.Handle, 0, 0);
                SetPedEyeColor(Game.PlayerPed.Handle, 0);
                ClearAllPedProps(Game.PlayerPed.Handle);

                currentCharacter = await AppySavedDataToPed(currentCharacter, Game.PlayerPed.Handle);
            }

            // Set the facial expression, or set it to 'normal' if it wasn't saved/set before.
            SetFacialIdleAnimOverride(Game.PlayerPed.Handle, currentCharacter.FacialExpression ?? facial_expressions[0], null);

            if (!string.IsNullOrEmpty(currentCharacter.SaveName))
            {
                SetResourceKvp("vmenu_last_character", GetMpCharacterKey(currentCharacter.SaveName));
                SaveMpCharacterSnapshot(currentCharacter);
            }
        }

        /// <summary>
        /// Creates the saved mp characters menu.
        /// </summary>
        private void CreateSavedPedsMenu()
        {
            UpdateSavedPedsMenu();

            void PopulateSavedCharactersCategoryMenu()
            {
                if (string.IsNullOrEmpty(currentCategory.Name))
                {
                    return;
                }

                bool isUncategorized = currentCategory.Name == "Uncategorized";

                savedCharactersCategoryMenu.MenuTitle = currentCategory.Name;
                savedCharactersCategoryMenu.MenuSubtitle = $"~s~Category: ~y~{currentCategory.Name}";
                savedCharactersCategoryMenu.ClearMenuItems();

                var iconNames = Enum.GetNames(typeof(MenuItem.Icon)).ToList();

                string ChangeCallback(MenuDynamicListItem item, bool left)
                {
                    int currentIndex = iconNames.IndexOf(item.CurrentItem);
                    int newIndex = left ? currentIndex - 1 : currentIndex + 1;

                    if (iconNames.ElementAtOrDefault(newIndex) == default)
                    {
                        newIndex = left ? iconNames.Count - 1 : 0;
                    }

                    item.RightIcon = (MenuItem.Icon)newIndex;

                    return iconNames[newIndex];
                }

                var renameBtn = new MenuItem("Rename Category", "Rename this category.")
                {
                    Enabled = !isUncategorized
                };
                var descriptionBtn = new MenuItem("Change Category Description", "Change this category's description.")
                {
                    Enabled = !isUncategorized
                };
                var iconBtn = new MenuDynamicListItem("Change Category Icon", iconNames[(int)currentCategory.Icon], new MenuDynamicListItem.ChangeItemCallback(ChangeCallback), "Change this category's icon. Select to save.")
                {
                    Enabled = !isUncategorized,
                    RightIcon = currentCategory.Icon
                };
                var deleteBtn = new MenuItem("Delete Category", "Delete this category. This can not be undone!")
                {
                    RightIcon = MenuItem.Icon.WARNING,
                    Enabled = !isUncategorized
                };
                var deleteCharsBtn = new MenuCheckboxItem("Delete All Characters", "If checked, when \"Delete Category\" is pressed, all the saved characters in this category will be deleted as well. If not checked, saved characters will be moved to \"Uncategorized\".")
                {
                    Enabled = !isUncategorized
                };

                savedCharactersCategoryMenu.AddMenuItem(renameBtn);
                savedCharactersCategoryMenu.AddMenuItem(descriptionBtn);
                savedCharactersCategoryMenu.AddMenuItem(iconBtn);
                savedCharactersCategoryMenu.AddMenuItem(deleteBtn);
                savedCharactersCategoryMenu.AddMenuItem(deleteCharsBtn);

                var spacer = GetSpacerMenuItem("↓ Characters ↓");
                savedCharactersCategoryMenu.AddMenuItem(spacer);

                List<string> names = GetAllMpCharacterNames();

                if (names.Count > 0)
                {
                    var defaultChar = GetResourceKvpString("vmenu_default_character") ?? "";

                    names.Sort((a, b) => a.ToLower().CompareTo(b.ToLower()));
                    foreach (var name in names)
                    {
                        var tmpData = StorageManager.GetSavedMpCharacterData("mp_ped_" + name);

                        if (string.IsNullOrEmpty(tmpData.Category))
                        {
                            if (!isUncategorized)
                            {
                                continue;
                            }
                        }
                        else if (tmpData.Category != currentCategory.Name)
                        {
                            continue;
                        }

                        var btn = new MenuItem(name, "Click to spawn, edit, clone, rename or delete this saved character.")
                        {
                            Label = "→→→",
                            LeftIcon = tmpData.IsMale ? MenuItem.Icon.MALE : MenuItem.Icon.FEMALE,
                            ItemData = tmpData.IsMale
                        };
                        if (defaultChar == "mp_ped_" + name)
                        {
                            btn.LeftIcon = MenuItem.Icon.TICK;
                            btn.Description += " ~g~This character is currently set as your default character and can be forced on join, respawn, or revive.";
                        }
                        savedCharactersCategoryMenu.AddMenuItem(btn);
                        MenuController.BindMenuItem(savedCharactersCategoryMenu, manageSavedCharacterMenu, btn);
                    }
                }

                savedCharactersCategoryMenu.RefreshIndex();
            }

            void RefreshSavedCharacterMenus(bool refreshCurrentCategory = true)
            {
                UpdateSavedPedsMenu();

                if (refreshCurrentCategory)
                {
                    PopulateSavedCharactersCategoryMenu();
                }
            }

            MenuController.AddMenu(manageSavedCharacterMenu);

            var spawnPed = new MenuItem("Spawn Saved Character", "Spawns the selected saved character.");
            editPedBtn = new MenuItem("Edit Saved Character", "This allows you to edit everything about your saved character. The changes will be saved to this character's save file entry once you hit the save button.");
            var clonePed = new MenuItem("Clone Saved Character", "This will make a clone of your saved character. It will ask you to provide a name for that character. If that name is already taken the action will be canceled.");
            var setAsDefaultPed = new MenuItem("Set As Default Character", "Choose this saved MP character as your default. If 'Respawn As Default MP Character' is enabled in Misc Settings, vMenu will force this character when you join, respawn, or revive.");
            var renameCharacter = new MenuItem("Rename Saved Character", "You can rename this saved character. If the name is already taken then the action will be canceled.");
            var saveCurrentPedAsCharacter = new MenuItem("Update Character Clothing", "This applies your current clothing to this saved ped. ~r~This will overwrite this saved ped's clothing.~w~ Only clothing is updated, no other appearance features.")
            {
                LeftIcon = MenuItem.Icon.WARNING
            };
            var delPed = new MenuItem("Delete Saved Character", "Deletes the selected saved character. This can not be undone!")
            {
                LeftIcon = MenuItem.Icon.WARNING
            };
            manageSavedCharacterMenu.AddMenuItem(spawnPed);
            manageSavedCharacterMenu.AddMenuItem(editPedBtn);
            manageSavedCharacterMenu.AddMenuItem(clonePed);
            manageSavedCharacterMenu.AddMenuItem(setCategoryBtn);
            manageSavedCharacterMenu.AddMenuItem(setAsDefaultPed);
            manageSavedCharacterMenu.AddMenuItem(renameCharacter);
            manageSavedCharacterMenu.AddMenuItem(saveCurrentPedAsCharacter);

            // The character's own outfits and hair & makeup styles, kept in their own KVPs (CharacterWardrobe.cs).
            var characterOutfitsMenu = new Menu("vMenu", "Character Outfits");
            var characterStylesMenu = new Menu("vMenu", "Hair & Makeup Styles");
            var characterOutfits = new MenuItem("Outfits", "This character's own outfits: save what you're wearing under a name, and change into one later.")
            {
                Label = "→→→"
            };
            var characterStyles = new MenuItem("Hair & Makeup Styles", "This character's own hair and makeup styles: save your hair, eyebrows, beard, makeup, blush and lipstick under a name, and apply one later.")
            {
                Label = "→→→"
            };
            MenuController.AddSubmenu(manageSavedCharacterMenu, characterOutfitsMenu);
            MenuController.AddSubmenu(manageSavedCharacterMenu, characterStylesMenu);
            manageSavedCharacterMenu.AddMenuItem(characterOutfits);
            manageSavedCharacterMenu.AddMenuItem(characterStyles);
            MenuController.BindMenuItem(manageSavedCharacterMenu, characterOutfitsMenu, characterOutfits);
            MenuController.BindMenuItem(manageSavedCharacterMenu, characterStylesMenu, characterStyles);
            CreateCharacterWardrobeMenu(characterOutfitsMenu, CharacterWardrobe.Kind.Outfit);
            CreateCharacterWardrobeMenu(characterStylesMenu, CharacterWardrobe.Kind.Style);

            manageSavedCharacterMenu.AddMenuItem(delPed);

            MenuController.BindMenuItem(manageSavedCharacterMenu, createCharacterMenu, editPedBtn);

            manageSavedCharacterMenu.OnItemSelect += async (sender, item, index) =>
            {
                if (item == editPedBtn)
                {
                    currentCharacter = StorageManager.GetSavedMpCharacterData(selectedSavedCharacterManageName);

                    await SpawnSavedPed(true);

                    MakeCreateCharacterMenu(male: currentCharacter.IsMale, editPed: true);
                }
                else if (item == spawnPed)
                {
                    currentCharacter = StorageManager.GetSavedMpCharacterData(selectedSavedCharacterManageName);

                    await SpawnSavedPed(true);
                }
                else if (item == clonePed)
                {
                    var tmpCharacter = StorageManager.GetSavedMpCharacterData("mp_ped_" + selectedSavedCharacterManageName);
                    var name = await GetUserInput(windowTitle: "Enter a name for the cloned character", defaultText: tmpCharacter.SaveName.Substring(7), maxInputLength: 30);
                    if (string.IsNullOrEmpty(name))
                    {
                        Notify.Error(CommonErrors.InvalidSaveName);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(GetResourceKvpString("mp_ped_" + name)))
                        {
                            Notify.Error(CommonErrors.SaveNameAlreadyExists);
                        }
                        else
                        {
                            tmpCharacter.SaveName = "mp_ped_" + name;
                            if (StorageManager.SaveJsonData("mp_ped_" + name, JsonConvert.SerializeObject(tmpCharacter), false))
                            {
                                // The clone gets copies of the character's own outfits and styles.
                                CharacterWardrobe.Transfer("mp_ped_" + selectedSavedCharacterManageName, "mp_ped_" + name, keepSource: true,
                                    KvpKeys, GetResourceKvpString, SetResourceKvp, DeleteResourceKvp);
                                Notify.Success($"Your character has been cloned. The name of the cloned character is: ~g~{name}~s~.");
                                MenuController.CloseAllMenus();
                                UpdateSavedPedsMenu();
                                savedCharactersMenu.OpenMenu();
                            }
                            else
                            {
                                Notify.Error("The clone could not be created, reason unknown. Does a character already exist with that name? :(");
                            }
                        }
                    }
                }
                else if (item == renameCharacter)
                {
                    var tmpCharacter = StorageManager.GetSavedMpCharacterData("mp_ped_" + selectedSavedCharacterManageName);
                    var name = await GetUserInput(windowTitle: "Enter a new character name", defaultText: tmpCharacter.SaveName.Substring(7), maxInputLength: 30);
                    if (string.IsNullOrEmpty(name))
                    {
                        Notify.Error(CommonErrors.InvalidInput);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(GetResourceKvpString("mp_ped_" + name)))
                        {
                            Notify.Error(CommonErrors.SaveNameAlreadyExists);
                        }
                        else
                        {
                            var oldPedKey = "mp_ped_" + selectedSavedCharacterManageName;
                            var newPedKey = "mp_ped_" + name;
                            tmpCharacter.SaveName = "mp_ped_" + name;
                            if (StorageManager.SaveJsonData(newPedKey, JsonConvert.SerializeObject(tmpCharacter), false))
                            {
                                RenameMpCharacterReferences(oldPedKey, newPedKey);
                                StorageManager.DeleteSavedStorageItem(oldPedKey);
                                selectedSavedCharacterManageName = name;
                                Notify.Success($"Your character has been renamed to ~g~{name}~s~.");
                                RefreshSavedCharacterMenus();
                                while (!MenuController.IsAnyMenuOpen())
                                {
                                    await BaseScript.Delay(0);
                                }
                                manageSavedCharacterMenu.GoBack();
                            }
                            else
                            {
                                Notify.Error("Something went wrong while renaming your character, your old character will NOT be deleted because of this.");
                            }
                        }
                    }
                }
                else if (item == saveCurrentPedAsCharacter)
                {
                    if (saveCurrentPedAsCharacter.Label == "Are you sure?")
                    {
                        saveCurrentPedAsCharacter.Label = "";
                        var tmpCharacter = StorageManager.GetSavedMpCharacterData("mp_ped_" + selectedSavedCharacterManageName);
                        if (tmpCharacter.ModelHash != (uint)GetEntityModel(Game.PlayerPed.Handle))
                        {
                            Notify.Error("You're not using this character's body type, so its clothing wasn't changed.");
                            return;
                        }

                        // Pieces of this character that couldn't be matched when it was put on keep
                        // their saved entry; everything else is what you're wearing now.
                        var wearingThisCharacter = GetMpCharacterKey(currentCharacter.SaveName) == GetMpCharacterKey(tmpCharacter.SaveName);
                        tmpCharacter = ReplacePedDataClothing(tmpCharacter, keepUnresolved: wearingThisCharacter);

                        if (StorageManager.SaveJsonData(tmpCharacter.SaveName, JsonConvert.SerializeObject(tmpCharacter), true))
                        {
                            if (GetResourceKvpString("vmenu_last_character") == GetMpCharacterKey(tmpCharacter.SaveName))
                            {
                                SaveMpCharacterSnapshot(tmpCharacter);
                            }
                            Notify.Success($"This character's clothing has been updated!");
                            UpdateSavedPedsMenu();
                        }
                        else
                        {
                            Notify.Error("Unable to update this character's clothing. The reason is unknown.");
                        }
                    }
                    else
                    {
                        saveCurrentPedAsCharacter.Label = "Are you sure?";
                    }
                }
                else if (item == delPed)
                {
                    if (delPed.Label == "Are you sure?")
                    {
                        delPed.Label = "";
                        var pedKey = "mp_ped_" + selectedSavedCharacterManageName;
                        DeleteResourceKvp(pedKey);
                        ClearMpCharacterReferences(pedKey);
                        Notify.Success("Your saved character has been deleted.");
                        RefreshSavedCharacterMenus();
                        manageSavedCharacterMenu.GoBack();
                        manageSavedCharacterMenu.RefreshIndex();
                    }
                    else
                    {
                        delPed.Label = "Are you sure?";
                    }
                }
                else if (item == setAsDefaultPed)
                {
                    var pedKey = "mp_ped_" + selectedSavedCharacterManageName;
                    if (GetResourceKvpString("vmenu_default_character") == pedKey)
                    {
                        DeleteResourceKvp("vmenu_default_character");
                        Notify.Success($"Your character ~b~{selectedSavedCharacterManageName}~s~ is no longer your default character. vMenu will restore your last MP outfit instead.");
                        setAsDefaultPed.Text = "Set As Default Character";
                        setAsDefaultPed.Description = "Choose this saved MP character as your default. If 'Respawn As Default MP Character' is enabled in Misc Settings, vMenu will force this character when you join, respawn, or revive.";
                    }
                    else
                    {
                        SetResourceKvp("vmenu_default_character", pedKey);
                        Notify.Success($"Your character ~b~{selectedSavedCharacterManageName}~s~ is now your default MP character.");
                        setAsDefaultPed.Text = "Unset Default Character";
                        setAsDefaultPed.Description = "This character is currently your chosen default. Click again to clear it and use last outfit restore instead.";
                    }
                }

                if (item != delPed)
                {
                    if (delPed.Label == "Are you sure?")
                    {
                        delPed.Label = "";
                    }
                }

                if (item != saveCurrentPedAsCharacter)
                {
                    if (saveCurrentPedAsCharacter.Label == "Are you sure?")
                    {
                        saveCurrentPedAsCharacter.Label = "";
                    }
                }
            };

            // Update category preview icon
            manageSavedCharacterMenu.OnListIndexChange += (_, listItem, _, newSelectionIndex, _) => listItem.RightIcon = listItem.ItemData[newSelectionIndex];

            // Update character's category
            manageSavedCharacterMenu.OnListItemSelect += async (_, listItem, listIndex, _) =>
            {
                var tmpCharacter = StorageManager.GetSavedMpCharacterData("mp_ped_" + selectedSavedCharacterManageName);

                string name = listItem.ListItems[listIndex];

                if (name == "Create New")
                {
                    var newName = await GetUserInput(windowTitle: "Enter a category name.", maxInputLength: 30);
                    if (string.IsNullOrEmpty(newName) || newName.ToLower() == "uncategorized" || newName.ToLower() == "create new")
                    {
                        Notify.Error(CommonErrors.InvalidInput);
                        return;
                    }
                    else
                    {
                        var description = await GetUserInput(windowTitle: "Enter a category description (optional).", maxInputLength: 120);
                        var newCategory = new MpCharacterCategory
                        {
                            Name = newName,
                            Description = description
                        };

                        if (StorageManager.SaveJsonData("mp_character_category_" + newName, JsonConvert.SerializeObject(newCategory), false))
                        {
                            Notify.Success($"Your category (~g~{newName}~s~) has been saved.");
                            Log($"Saved Category {newName}.");
                            MenuController.CloseAllMenus();
                            UpdateSavedPedsMenu();
                            savedCharactersCategoryMenu.OpenMenu();

                            currentCategory = newCategory;
                            name = newName;
                        }
                        else
                        {
                            Notify.Error($"Saving failed, most likely because this name (~y~{newName}~s~) is already in use.");
                            return;
                        }
                    }
                }

                tmpCharacter.Category = name;

                var json = JsonConvert.SerializeObject(tmpCharacter);
                if (StorageManager.SaveJsonData(tmpCharacter.SaveName, json, true))
                {
                    Notify.Success("Your character was saved successfully.");
                }
                else
                {
                    Notify.Error("Your character could not be saved. Reason unknown. :(");
                }

                MenuController.CloseAllMenus();
                UpdateSavedPedsMenu();
                savedCharactersMenu.OpenMenu();
            };

            // reset the "are you sure" state.
            manageSavedCharacterMenu.OnMenuClose += (sender) =>
            {
                foreach (MenuItem item in manageSavedCharacterMenu.GetMenuItems())
                {
                    if (item.Label == "Are you sure?")
                    {
                        item.Label = "";
                    }
                }
            };

            // Load selected category
            savedCharactersMenu.OnItemSelect += async (sender, item, index) =>
            {
                // Create new category
                if (item.ItemData is not MpCharacterCategory)
                {
                    var name = await GetUserInput(windowTitle: "Enter a category name.", maxInputLength: 30);
                    if (string.IsNullOrEmpty(name) || name.ToLower() == "uncategorized" || name.ToLower() == "create new")
                    {
                        Notify.Error(CommonErrors.InvalidInput);
                        return;
                    }
                    else
                    {
                        var description = await GetUserInput(windowTitle: "Enter a category description (optional).", maxInputLength: 120);
                        var newCategory = new MpCharacterCategory
                        {
                            Name = name,
                            Description = description
                        };

                        if (StorageManager.SaveJsonData("mp_character_category_" + name, JsonConvert.SerializeObject(newCategory), false))
                        {
                            Notify.Success($"Your category (~g~{name}~s~) has been saved.");
                            Log($"Saved Category {name}.");
                            MenuController.CloseAllMenus();
                            UpdateSavedPedsMenu();
                            savedCharactersCategoryMenu.OpenMenu();

                            currentCategory = newCategory;
                        }
                        else
                        {
                            Notify.Error($"Saving failed, most likely because this name (~y~{name}~s~) is already in use.");
                            return;
                        }
                    }
                }
                // Select an old category
                else
                {
                    currentCategory = item.ItemData;
                }

                bool isUncategorized = currentCategory.Name == "Uncategorized";

                savedCharactersCategoryMenu.MenuTitle = currentCategory.Name;
                savedCharactersCategoryMenu.MenuSubtitle = $"~s~Category: ~y~{currentCategory.Name}";
                savedCharactersCategoryMenu.ClearMenuItems();

                var iconNames = Enum.GetNames(typeof(MenuItem.Icon)).ToList();

                string ChangeCallback(MenuDynamicListItem item, bool left)
                {
                    int currentIndex = iconNames.IndexOf(item.CurrentItem);
                    int newIndex = left ? currentIndex - 1 : currentIndex + 1;

                    // If going past the start or end of the list
                    if (iconNames.ElementAtOrDefault(newIndex) == default)
                    {
                        if (left)
                        {
                            newIndex = iconNames.Count - 1;
                        }
                        else
                        {
                            newIndex = 0;
                        }
                    }

                    item.RightIcon = (MenuItem.Icon)newIndex;

                    return iconNames[newIndex];
                }

                var renameBtn = new MenuItem("Rename Category", "Rename this category.")
                {
                    Enabled = !isUncategorized
                };
                var descriptionBtn = new MenuItem("Change Category Description", "Change this category's description.")
                {
                    Enabled = !isUncategorized
                };
                var iconBtn = new MenuDynamicListItem("Change Category Icon", iconNames[(int)currentCategory.Icon], new MenuDynamicListItem.ChangeItemCallback(ChangeCallback), "Change this category's icon. Select to save.")
                {
                    Enabled = !isUncategorized,
                    RightIcon = currentCategory.Icon
                };
                var deleteBtn = new MenuItem("Delete Category", "Delete this category. This can not be undone!")
                {
                    RightIcon = MenuItem.Icon.WARNING,
                    Enabled = !isUncategorized
                };
                var deleteCharsBtn = new MenuCheckboxItem("Delete All Characters", "If checked, when \"Delete Category\" is pressed, all the saved characters in this category will be deleted as well. If not checked, saved characters will be moved to \"Uncategorized\".")
                {
                    Enabled = !isUncategorized
                };

                savedCharactersCategoryMenu.AddMenuItem(renameBtn);
                savedCharactersCategoryMenu.AddMenuItem(descriptionBtn);
                savedCharactersCategoryMenu.AddMenuItem(iconBtn);
                savedCharactersCategoryMenu.AddMenuItem(deleteBtn);
                savedCharactersCategoryMenu.AddMenuItem(deleteCharsBtn);

                var spacer = GetSpacerMenuItem("↓ Characters ↓");
                savedCharactersCategoryMenu.AddMenuItem(spacer);

                List<string> names = GetAllMpCharacterNames();

                if (names.Count > 0)
                {
                    var defaultChar = GetResourceKvpString("vmenu_default_character") ?? "";

                    names.Sort((a, b) => a.ToLower().CompareTo(b.ToLower()));
                    foreach (var name in names)
                    {
                        var tmpData = StorageManager.GetSavedMpCharacterData("mp_ped_" + name);

                        if (string.IsNullOrEmpty(tmpData.Category))
                        {
                            if (!isUncategorized)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (tmpData.Category != currentCategory.Name)
                            {
                                continue;
                            }
                        }

                        var btn = new MenuItem(name, "Click to spawn, edit, clone, rename or delete this saved character.")
                        {
                            Label = "→→→",
                            LeftIcon = tmpData.IsMale ? MenuItem.Icon.MALE : MenuItem.Icon.FEMALE,
                            ItemData = tmpData.IsMale
                        };
                        if (defaultChar == "mp_ped_" + name)
                        {
                            btn.LeftIcon = MenuItem.Icon.TICK;
                            btn.Description += " ~g~This character is currently set as your default character and can be forced on join, respawn, or revive.";
                        }
                        savedCharactersCategoryMenu.AddMenuItem(btn);
                        MenuController.BindMenuItem(savedCharactersCategoryMenu, manageSavedCharacterMenu, btn);
                    }
                }
            };

            savedCharactersCategoryMenu.OnIndexChange += async (menu, oldItem, newItem, oldIndex, newIndex) =>
            {
                if (!GetSettingsBool(Setting.vmenu_mp_ped_preview) || !MainMenu.MiscSettingsMenu.MPPedPreviews)
                {
                    return;
                }

                if (Entity.Exists(_clone))
                {
                    _clone.Delete();
                }

                if (newItem.ItemData == null)
                {
                    return;
                }

                MultiplayerPedData character = StorageManager.GetSavedMpCharacterData(newItem.Text);

                if (!await LoadModelAsync(character.ModelHash))
                {
                    Notify.Error(CommonErrors.CouldNotLoad, placeholderValue: "the character model");
                    return;
                }

                Ped playerPed = Game.PlayerPed;
                Vector3 clientPedPosition = playerPed.Position;

                _clone = new Ped(CreatePed(26, character.ModelHash, clientPedPosition.X, clientPedPosition.Y, clientPedPosition.Z - 3f, playerPed.Heading, false, false))
                {
                    IsCollisionEnabled = false,
                    IsInvincible = true,
                    BlockPermanentEvents = true,
                    IsPositionFrozen = true
                };

                int cloneHandle = _clone.Handle;

                await AppySavedDataToPed(character, cloneHandle, notifyClothingIssues: false);

                SetEntityCanBeDamaged(cloneHandle, false);
                SetPedAoBlobRendering(cloneHandle, false);

                while (Entity.Exists(_clone))
                {
                    Vector3 worldCoord = Vector3.Zero;
                    Vector3 normal = Vector3.Zero;

                    GetWorldCoordFromScreenCoord(0.6f, 0.8f, ref worldCoord, ref normal);

                    Vector3 cameraRotation = GameplayCamera.Rotation;

                    _clone.Position = worldCoord + (normal * 3.5f);
                    _clone.Rotation = new Vector3(cameraRotation.X * -1, 0f, cameraRotation.Z + 180);
                    _clone.Heading = cameraRotation.Z + 180;

                    GameplayCamera.ClampPitch(0f, 0f);

                    await Delay(0);
                }
            };
            savedCharactersCategoryMenu.OnItemSelect += async (sender, item, index) =>
            {
                switch (index)
                {
                    // Rename Category
                    case 0:
                        var name = await GetUserInput(windowTitle: "Enter a new category name", defaultText: currentCategory.Name, maxInputLength: 30);

                        if (string.IsNullOrEmpty(name) || name.ToLower() == "uncategorized" || name.ToLower() == "create new")
                        {
                            Notify.Error(CommonErrors.InvalidInput);
                            return;
                        }
                        else if (GetAllCategoryNames().Contains(name) || !string.IsNullOrEmpty(GetResourceKvpString("mp_character_category_" + name)))
                        {
                            Notify.Error(CommonErrors.SaveNameAlreadyExists);
                            return;
                        }

                        string oldName = currentCategory.Name;

                        currentCategory.Name = name;

                        if (StorageManager.SaveJsonData("mp_character_category_" + name, JsonConvert.SerializeObject(currentCategory), false))
                        {
                            StorageManager.DeleteSavedStorageItem("mp_character_category_" + oldName);

                            int totalCount = 0;
                            int updatedCount = 0;
                            List<string> characterNames = GetAllMpCharacterNames();

                            if (characterNames.Count > 0)
                            {
                                foreach (var characterName in characterNames)
                                {
                                    var tmpData = StorageManager.GetSavedMpCharacterData("mp_ped_" + characterName);

                                    if (string.IsNullOrEmpty(tmpData.Category))
                                    {
                                        continue;
                                    }

                                    if (tmpData.Category != oldName)
                                    {
                                        continue;
                                    }

                                    totalCount++;

                                    tmpData.Category = name;

                                    if (StorageManager.SaveJsonData(tmpData.SaveName, JsonConvert.SerializeObject(tmpData), true))
                                    {
                                        updatedCount++;
                                        Log($"Updated category for \"{tmpData.SaveName}\"");
                                    }
                                    else
                                    {
                                        Log($"Something went wrong when updating category for \"{tmpData.SaveName}\"");
                                    }
                                }
                            }

                            Notify.Success($"Your category has been renamed to ~g~{name}~s~. {updatedCount}/{totalCount} characters updated.");
                            MenuController.CloseAllMenus();
                            UpdateSavedPedsMenu();
                            savedCharactersMenu.OpenMenu();
                        }
                        else
                        {
                            Notify.Error("Something went wrong while renaming your category, your old category will NOT be deleted because of this.");
                        }
                        break;

                    // Change Category Description
                    case 1:
                        var description = await GetUserInput(windowTitle: "Enter a new category description", defaultText: currentCategory.Description, maxInputLength: 120);

                        currentCategory.Description = description;

                        if (StorageManager.SaveJsonData("mp_character_category_" + currentCategory.Name, JsonConvert.SerializeObject(currentCategory), true))
                        {
                            Notify.Success($"Your category description has been changed.");
                            MenuController.CloseAllMenus();
                            UpdateSavedPedsMenu();
                            savedCharactersMenu.OpenMenu();
                        }
                        else
                        {
                            Notify.Error("Something went wrong while changing your category description.");
                        }
                        break;

                    // Delete Category
                    case 3:
                        if (item.Label == "Are you sure?")
                        {
                            bool deletePeds = (sender.GetMenuItems().ElementAt(4) as MenuCheckboxItem).Checked;

                            item.Label = "";
                            DeleteResourceKvp("mp_character_category_" + currentCategory.Name);

                            int totalCount = 0;
                            int updatedCount = 0;

                            List<string> characterNames = GetAllMpCharacterNames();

                            if (characterNames.Count > 0)
                            {
                                foreach (var characterName in characterNames)
                                {
                                    var tmpData = StorageManager.GetSavedMpCharacterData("mp_ped_" + characterName);

                                    if (string.IsNullOrEmpty(tmpData.Category))
                                    {
                                        continue;
                                    }

                                    if (tmpData.Category != currentCategory.Name)
                                    {
                                        continue;
                                    }

                                    totalCount++;

                                    if (deletePeds)
                                    {
                                        updatedCount++;

                                        var pedKey = GetMpCharacterKey(tmpData.SaveName);
                                        DeleteResourceKvp(pedKey);
                                        ClearMpCharacterReferences(pedKey);
                                    }
                                    else
                                    {
                                        tmpData.Category = "Uncategorized";

                                        if (StorageManager.SaveJsonData(tmpData.SaveName, JsonConvert.SerializeObject(tmpData), true))
                                        {
                                            updatedCount++;
                                            Log($"Updated category for \"{tmpData.SaveName}\"");
                                        }
                                        else
                                        {
                                            Log($"Something went wrong when updating category for \"{tmpData.SaveName}\"");
                                        }
                                    }
                                }
                            }

                            Notify.Success($"Your saved category has been deleted. {updatedCount}/{totalCount} characters {(deletePeds ? "deleted" : "updated")}.");
                            MenuController.CloseAllMenus();
                            UpdateSavedPedsMenu();
                            savedCharactersMenu.OpenMenu();
                        }
                        else
                        {
                            item.Label = "Are you sure?";
                        }
                        break;

                    // Load saved character menu
                    default:
                        List<string> categoryNames = GetAllCategoryNames();
                        List<MenuItem.Icon> categoryIcons = GetCategoryIcons(categoryNames);
                        int nameIndex = categoryNames.IndexOf(currentCategory.Name);

                        setCategoryBtn.ItemData = categoryIcons;
                        setCategoryBtn.ListItems = categoryNames;
                        setCategoryBtn.ListIndex = nameIndex == 1 ? 0 : nameIndex;
                        setCategoryBtn.RightIcon = categoryIcons[setCategoryBtn.ListIndex];
                        selectedSavedCharacterManageName = item.Text;
                        manageSavedCharacterMenu.MenuSubtitle = item.Text;
                        manageSavedCharacterMenu.CounterPreText = $"{(item.LeftIcon == MenuItem.Icon.MALE ? "(Male)" : "(Female)")} ";

                        if (GetResourceKvpString("vmenu_default_character") == "mp_ped_" + item.Text)
                        {
                            setAsDefaultPed.Text = "Unset Default Character";
                            setAsDefaultPed.Description = "This character is currently your chosen default. Click again to clear it and use last outfit restore instead.";
                        }
                        else
                        {
                            setAsDefaultPed.Text = "Set As Default Character";
                            setAsDefaultPed.Description = "Choose this saved MP character as your default. If 'Respawn As Default MP Character' is enabled in Misc Settings, vMenu will force this character when you join, respawn, or revive.";
                        }

                        manageSavedCharacterMenu.RefreshIndex();
                        break;
                }
            };

            // Change Category Icon
            savedCharactersCategoryMenu.OnDynamicListItemSelect += (_, _, currentItem) =>
            {
                var iconNames = Enum.GetNames(typeof(MenuItem.Icon)).ToList();
                int iconIndex = iconNames.IndexOf(currentItem);

                currentCategory.Icon = (MenuItem.Icon)iconIndex;

                if (StorageManager.SaveJsonData("mp_character_category_" + currentCategory.Name, JsonConvert.SerializeObject(currentCategory), true))
                {
                    Notify.Success($"Your category icon been changed to ~g~{iconNames[iconIndex]}~s~.");
                    UpdateSavedPedsMenu();
                }
                else
                {
                    Notify.Error("Something went wrong while changing your category icon.");
                }
            };

            savedCharactersCategoryMenu.OnMenuClose += (_) =>
            {
                if (Entity.Exists(_clone))
                {
                    _clone.Delete();
                }
            };

        }

        /// <summary>
        /// Updates the saved peds menu.
        /// </summary>
        private void UpdateSavedPedsMenu()
        {
            var categories = GetAllCategoryNames();

            savedCharactersMenu.ClearMenuItems();

            var createCategoryBtn = new MenuItem("Create Category", "Create a new character category.")
            {
                Label = "→→→"
            };
            savedCharactersMenu.AddMenuItem(createCategoryBtn);

            var spacer = GetSpacerMenuItem("↓ Character Categories ↓");
            savedCharactersMenu.AddMenuItem(spacer);

            var uncategorized = new MpCharacterCategory
            {
                Name = "Uncategorized",
                Description = "All saved MP Characters that have not been assigned to a category."
            };
            var uncategorizedBtn = new MenuItem(uncategorized.Name, uncategorized.Description)
            {
                Label = "→→→",
                ItemData = uncategorized
            };
            savedCharactersMenu.AddMenuItem(uncategorizedBtn);
            MenuController.BindMenuItem(savedCharactersMenu, savedCharactersCategoryMenu, uncategorizedBtn);

            // Remove "Create New" and "Uncategorized"
            categories.RemoveRange(0, 2);

            if (categories.Count > 0)
            {
                categories.Sort((a, b) => a.ToLower().CompareTo(b.ToLower()));
                foreach (var item in categories)
                {
                    MpCharacterCategory category = StorageManager.GetSavedMpCharacterCategoryData("mp_character_category_" + item);

                    var btn = new MenuItem(category.Name, category.Description)
                    {
                        Label = "→→→",
                        LeftIcon = category.Icon,
                        ItemData = category
                    };
                    savedCharactersMenu.AddMenuItem(btn);
                    MenuController.BindMenuItem(savedCharactersMenu, savedCharactersCategoryMenu, btn);
                }
            }

            savedCharactersMenu.RefreshIndex();
        }

        private List<string> GetAllCategoryNames()
        {
            var categories = new List<string>();
            var handle = StartFindKvp("mp_character_category_");
            while (true)
            {
                var foundCategory = FindKvp(handle);
                if (string.IsNullOrEmpty(foundCategory))
                {
                    break;
                }
                else
                {
                    categories.Add(foundCategory.Substring(22));
                }
            }
            EndFindKvp(handle);

            categories.Insert(0, "Create New");
            categories.Insert(1, "Uncategorized");

            return categories;
        }

        private List<MenuItem.Icon> GetCategoryIcons(List<string> categoryNames)
        {
            List<MenuItem.Icon> icons = new List<MenuItem.Icon> { };

            foreach (var name in categoryNames)
            {
                icons.Add(StorageManager.GetSavedMpCharacterCategoryData("mp_character_category_" + name).Icon);
            }

            return icons;
        }

        private List<string> GetAllMpCharacterNames()
        {
            var names = new List<string>();
            var handle = StartFindKvp("mp_ped_");
            while (true)
            {
                var foundName = FindKvp(handle);
                if (string.IsNullOrEmpty(foundName))
                {
                    break;
                }
                else
                {
                    names.Add(foundName.Substring(7));
                }
            }
            EndFindKvp(handle);

            return names;
        }

        /// <summary>
        /// Saved Outfits: clothes and props only, for the freemode model you are now. Wearing one
        /// also updates the character being edited, so saving the character keeps it.
        /// </summary>
        private void CreateSavedOutfitsMenu(Menu outfitsMenu)
        {
            var saveCurrent = new MenuItem("Save Current Outfit", "Saves the clothes and props you're wearing under a name.");

            Dictionary<string, SavedOutfit> LoadOutfits(uint model)
            {
                var outfits = new Dictionary<string, SavedOutfit>();
                var handle = StartFindKvp(SavedOutfit.KeyPrefix);
                while (true)
                {
                    var key = FindKvp(handle);
                    if (string.IsNullOrEmpty(key))
                    {
                        break;
                    }
                    var outfit = SavedOutfit.Parse(GetResourceKvpString(key));
                    if (outfit != null && outfit.Model == model)
                    {
                        outfits[key.Substring(SavedOutfit.KeyPrefix.Length)] = outfit;
                    }
                }
                EndFindKvp(handle);
                return outfits;
            }

            void Rebuild()
            {
                outfitsMenu.ClearMenuItems();
                if (!IsCurrentPedMpFreemode())
                {
                    outfitsMenu.AddMenuItem(new MenuItem("Only for MP characters", "Load or create an MP character to save and wear outfits.") { Enabled = false });
                    outfitsMenu.RefreshIndex();
                    return;
                }
                outfitsMenu.AddMenuItem(saveCurrent);
                var outfits = LoadOutfits((uint)GetEntityModel(Game.PlayerPed.Handle));
                if (outfits.Count == 0)
                {
                    outfitsMenu.AddMenuItem(new MenuItem("No outfits yet", "Outfits are kept per body type: male characters share theirs, and so do female ones.") { Enabled = false });
                }
                foreach (var name in outfits.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    outfitsMenu.AddMenuItem(new MenuListItem(name, new List<string> { "Wear", "Update", "Delete" }, 0,
                        "Wear changes into it. Update replaces it with what you're wearing. Choose one, then press Enter.") { ItemData = name });
                }
                outfitsMenu.RefreshIndex();
            }

            outfitsMenu.OnMenuOpen += (m) => Rebuild();

            outfitsMenu.OnItemSelect += async (sender, item, index) =>
            {
                if (item != saveCurrent || !IsCurrentPedMpFreemode())
                {
                    return;
                }
                var name = (await GetUserInput("Outfit name", 30) ?? "").Trim();
                if (string.IsNullOrEmpty(name))
                {
                    Notify.Error(CommonErrors.InvalidSaveName);
                    return;
                }
                if (!string.IsNullOrEmpty(GetResourceKvpString(SavedOutfit.KeyFor(name))))
                {
                    Notify.Error($"You already have an outfit called {name}. Choose Update on it to replace it.");
                    return;
                }
                SetResourceKvp(SavedOutfit.KeyFor(name), CaptureCurrentOutfit().ToJson());
                Notify.Success($"Outfit {name} saved.");
                Rebuild();
            };

            outfitsMenu.OnListItemSelect += async (sender, item, listIndex, itemIndex) =>
            {
                if (item.ItemData is not string name || !IsCurrentPedMpFreemode())
                {
                    return;
                }
                var key = SavedOutfit.KeyFor(name);
                if (listIndex == 0)
                {
                    var save = ClothingSaves.Read(ClothingSaves.Kind.Outfit, GetResourceKvpString(key));
                    if (!WearOutfit(save, $"Outfit {name}"))
                    {
                        Notify.Error("This outfit can't be worn by this character.");
                        return;
                    }
                    Notify.Success($"Changed into {name}. Save your character to make it their usual look.");
                }
                else if (listIndex == 1)
                {
                    SetResourceKvp(key, CaptureCurrentOutfit().ToJson());
                    Notify.Success($"Outfit {name} updated with what you're wearing.");
                }
                else if (await GetUserConfirmation("Delete Outfit", $"Delete the outfit **{name}**?"))
                {
                    DeleteResourceKvp(key);
                    Notify.Success($"Outfit {name} deleted.");
                    Rebuild();
                }
            };
        }

        /// <summary>
        /// What the player ped wears now, as an outfit in the Saved Outfits format: a collection
        /// reference per slot, and the global numbers stamped with the running layout. Saved Outfits,
        /// characters' own outfits and department outfits are all saved with this.
        /// </summary>
        private static SavedOutfit CaptureCurrentOutfit()
        {
            var ped = Game.PlayerPed.Handle;
            var runtime = ClothingService.Runtime(ped);
            return SavedOutfit.Capture((uint)GetEntityModel(ped),
                slot => GetPedDrawableVariation(ped, slot), slot => GetPedTextureVariation(ped, slot),
                slot => GetPedPropIndex(ped, slot), slot => GetPedPropTextureIndex(ped, slot),
                (slot, global, texture) => ClothingResolver.Capture(slot, global, texture, ClothingService.Layouts, runtime),
                runtime.LayoutPrint);
        }

        /// <summary>
        /// Puts an outfit (Saved Outfits, a character's own outfit, a department outfit) on the player
        /// ped through the shared clothing resolver, and records it in the character being worn or
        /// edited, so saving the character keeps it. False when the outfit isn't for this body.
        /// </summary>
        private bool WearOutfit(ClothingSave save, string what)
        {
            var ped = Game.PlayerPed.Handle;
            if (save == null || save.Model != (uint)GetEntityModel(ped))
            {
                return false;
            }
            var resolution = ClothingService.Resolve(save, ClothingResolver.OutfitSlots, ped);
            ClothingService.Apply(resolution, ped, 0);
            // Slots the outfit set are now the player's choice; unmatched ones keep what was worn.
            foreach (var piece in resolution.Pieces)
            {
                unresolvedSlots.Remove(piece.Slot);
            }
            currentCharacter = ReplacePedDataClothing(currentCharacter, keepUnresolved: true);
            ClothingService.Report(what, resolution, true);
            return true;
        }

        /// <summary>
        /// Department Outfits: uniforms published for the org you're on duty with (PoliceEMSActivity)
        /// and, for managers, the orgs they manage, for the body type you are now. The server checks
        /// every request (build/vMenu/server/deptOutfits.lua); see DepartmentOutfits.cs.
        /// </summary>
        private void CreateDepartmentOutfitsMenu(Menu deptMenu)
        {
            var generation = 0;
            var busy = false;
            DepartmentOutfitListing listing = null;

            MenuItem Info(string text, string description = null) => new(text, description) { Enabled = false };

            void Show(List<MenuItem> items)
            {
                deptMenu.ClearMenuItems();
                foreach (var item in items)
                {
                    deptMenu.AddMenuItem(item);
                }
                deptMenu.RefreshIndex();
            }

            void Build()
            {
                var items = new List<MenuItem>();
                if (listing.Duty != null)
                {
                    items.Add(Info($"On duty: {listing.Duty.Label}", "Your department's outfits for this body type are listed below."));
                }
                else
                {
                    items.Add(Info("Off duty", listing.DutyAvailable
                        ? "Go on duty with your department to see its outfits here."
                        : "Duty status isn't available on this server right now, so only departments you manage are shown."));
                }

                if (listing.Manage.Count == 1)
                {
                    items.Add(new MenuItem("Publish Current Outfit", $"Publishes the clothes and props you're wearing as a {listing.Manage[0].Label} outfit for this body type.")
                    {
                        ItemData = listing.Manage[0].Id,
                    });
                }
                else if (listing.Manage.Count > 1)
                {
                    var start = Math.Max(0, listing.Manage.FindIndex(m => m.Id == listing.Duty?.Id));
                    items.Add(new MenuListItem("Publish Current Outfit", listing.Manage.Select(m => m.Label).ToList(), start,
                        "Choose the department, then press Enter to publish the clothes and props you're wearing as its outfit for this body type.")
                    {
                        ItemData = listing.Manage.Select(m => m.Id).ToList(),
                    });
                }

                var sections = listing.Sections();
                foreach (var org in sections)
                {
                    if (sections.Count > 1 || listing.Manage.Count > 0)
                    {
                        items.Add(GetSpacerMenuItem(org.Label));
                    }
                    var outfits = listing.OutfitsOf(org.Id);
                    if (outfits.Count == 0)
                    {
                        items.Add(Info("No outfits yet", "None are published for this body type."));
                    }
                    foreach (var outfit in outfits)
                    {
                        var about = $"{org.Label} outfit{(outfit.By != null ? $" by {outfit.By}" : "")}{(outfit.Updated != null ? $", updated {outfit.Updated}" : "")}.";
                        items.Add(outfit.CanManage
                            ? new MenuListItem(outfit.Name, new List<string> { "Wear", "Replace", "Rename", "Delete" }, 0,
                                about + " Choose one, then press Enter. Replace swaps it for what you're wearing.") { ItemData = outfit }
                            : new MenuItem(outfit.Name, about + " Press Enter to wear it.") { ItemData = outfit });
                    }
                }
                Show(items);
            }

            async Task Reload()
            {
                var mine = ++generation;
                if (!IsCurrentPedMpFreemode())
                {
                    listing = null;
                    Show(new List<MenuItem> { Info("Only for MP characters", "Load or create an MP character to wear department outfits.") });
                    return;
                }
                Show(new List<MenuItem> { Info("Loading...") });
                var reply = await DepartmentOutfitsBridge.Request("list", new JObject { ["model"] = (long)(uint)GetEntityModel(Game.PlayerPed.Handle) });
                if (mine != generation)
                {
                    return; // opened again meanwhile; that request draws the menu
                }
                if (!reply.Ok)
                {
                    listing = null;
                    Show(new List<MenuItem> { Info("Couldn't load department outfits", reply.Message ?? "Close and reopen this menu to try again.") });
                    return;
                }
                listing = DepartmentOutfitListing.From(reply);
                Build();
            }

            async Task Act(Func<Task> action)
            {
                if (busy)
                {
                    Notify.Info("Please wait for your last department outfit request to finish.");
                    return;
                }
                busy = true;
                try
                {
                    await action();
                }
                finally
                {
                    busy = false;
                }
            }

            string LabelOf(string org) => listing?.LabelOf(org) ?? org;

            SavedOutfit CapturePublishable()
            {
                if (!IsCurrentPedMpFreemode())
                {
                    Notify.Error("Department outfits are only for MP characters.");
                    return null;
                }
                var outfit = CaptureCurrentOutfit();
                var missing = DepartmentOutfits.MissingRefs(outfit);
                if (missing.Count > 0)
                {
                    Notify.Error($"{string.Join(", ", missing.Take(3).Select(s => s.DisplayName))} can't be matched to a clothing collection, so this outfit can't be shared with a department.");
                    return null;
                }
                return outfit;
            }

            async Task Wear(DepartmentOutfitEntry outfit)
            {
                var reply = await DepartmentOutfitsBridge.Request("wear", new JObject { ["id"] = outfit.Id });
                if (!reply.Ok)
                {
                    Notify.Error(reply.Message ?? "This outfit can't be worn.");
                    return;
                }
                var save = ClothingSaves.Read(ClothingSaves.Kind.Outfit, reply.Text("payload"));
                if (!IsCurrentPedMpFreemode())
                {
                    Notify.Error("Department outfits are only for MP characters.");
                    return;
                }
                if (save == null)
                {
                    Notify.Error("This outfit couldn't be read. Ask a department head to publish it again.");
                    return;
                }
                if (!WearOutfit(save, $"Department outfit {outfit.Name}"))
                {
                    Notify.Error("This outfit is for the other body type.");
                    return;
                }
                Notify.Success($"Changed into {outfit.Name}.");
            }

            async Task Publish(string org)
            {
                var name = DepartmentOutfits.CleanName(await GetUserInput($"{LabelOf(org)} outfit name", DepartmentOutfits.MaxNameLength), out var error);
                if (name == null)
                {
                    Notify.Error(error);
                    return;
                }
                var outfit = CapturePublishable();
                if (outfit == null)
                {
                    return;
                }
                var reply = await DepartmentOutfitsBridge.Request("publish", new JObject { ["org"] = org, ["name"] = name, ["payload"] = outfit.ToJson() });
                if (!reply.Ok)
                {
                    Notify.Error(reply.Message ?? "The outfit couldn't be published.");
                    return;
                }
                Notify.Success(reply.Message ?? $"{name} published for {LabelOf(org)}.");
                await Reload();
            }

            async Task Replace(DepartmentOutfitEntry entry)
            {
                if (!await GetUserConfirmation("Replace Department Outfit", $"Replace **{entry.Name}** ({LabelOf(entry.Org)}) with the clothes and props you're wearing? Everyone who wears it from now on gets the new version."))
                {
                    return;
                }
                var outfit = CapturePublishable();
                if (outfit == null)
                {
                    return;
                }
                var reply = await DepartmentOutfitsBridge.Request("replace", new JObject { ["id"] = entry.Id, ["payload"] = outfit.ToJson() });
                if (!reply.Ok)
                {
                    Notify.Error(reply.Message ?? "The outfit couldn't be replaced.");
                    return;
                }
                Notify.Success(reply.Message ?? $"{entry.Name} replaced.");
                await Reload();
            }

            async Task Rename(DepartmentOutfitEntry entry)
            {
                var name = DepartmentOutfits.CleanName(await GetUserInput($"Rename {entry.Name}", entry.Name, DepartmentOutfits.MaxNameLength), out var error);
                if (name == null)
                {
                    Notify.Error(error);
                    return;
                }
                if (name == entry.Name)
                {
                    return;
                }
                var reply = await DepartmentOutfitsBridge.Request("rename", new JObject { ["id"] = entry.Id, ["name"] = name });
                if (!reply.Ok)
                {
                    Notify.Error(reply.Message ?? "The outfit couldn't be renamed.");
                    return;
                }
                Notify.Success(reply.Message ?? $"{entry.Name} renamed to {name}.");
                await Reload();
            }

            async Task Delete(DepartmentOutfitEntry entry)
            {
                if (!await GetUserConfirmation("Delete Department Outfit", $"Delete **{entry.Name}** ({LabelOf(entry.Org)}) for everyone in that department? This can't be undone."))
                {
                    return;
                }
                var reply = await DepartmentOutfitsBridge.Request("delete", new JObject { ["id"] = entry.Id });
                if (!reply.Ok)
                {
                    Notify.Error(reply.Message ?? "The outfit couldn't be deleted.");
                    return;
                }
                Notify.Success(reply.Message ?? $"{entry.Name} deleted.");
                await Reload();
            }

            // Refreshed every time it opens: duty and presets change while you play.
            deptMenu.OnMenuOpen += async (m) => await Reload();

            deptMenu.OnItemSelect += async (sender, item, index) =>
            {
                if (item is MenuListItem)
                {
                    return;
                }
                if (item.ItemData is DepartmentOutfitEntry outfit)
                {
                    await Act(() => Wear(outfit));
                }
                else if (item.ItemData is string org)
                {
                    await Act(() => Publish(org));
                }
            };

            deptMenu.OnListItemSelect += async (sender, item, listIndex, itemIndex) =>
            {
                if (item.ItemData is DepartmentOutfitEntry outfit)
                {
                    switch (listIndex)
                    {
                        case 0:
                            await Act(() => Wear(outfit));
                            break;
                        case 1:
                            await Act(() => Replace(outfit));
                            break;
                        case 2:
                            await Act(() => Rename(outfit));
                            break;
                        case 3:
                            await Act(() => Delete(outfit));
                            break;
                    }
                }
                else if (item.ItemData is List<string> orgs && listIndex >= 0 && listIndex < orgs.Count)
                {
                    await Act(() => Publish(orgs[listIndex]));
                }
            };
        }

        /// <summary>Every KVP key starting with <paramref name="prefix"/>.</summary>
        private static List<string> KvpKeys(string prefix)
        {
            var keys = new List<string>();
            var handle = StartFindKvp(prefix);
            if (handle == -1)
            {
                return keys;
            }
            while (true)
            {
                var key = FindKvp(handle);
                if (string.IsNullOrEmpty(key))
                {
                    break;
                }
                keys.Add(key);
            }
            EndFindKvp(handle);
            return keys;
        }

        /// <summary>True when the player ped is this saved character, loaded through vMenu.</summary>
        private bool IsWearingCharacter(string characterKey) =>
            !string.IsNullOrEmpty(currentCharacter.SaveName) && GetMpCharacterKey(currentCharacter.SaveName) == GetMpCharacterKey(characterKey)
            && currentCharacter.ModelHash != 0 && (uint)GetEntityModel(Game.PlayerPed.Handle) == currentCharacter.ModelHash;

        /// <summary>Spawns the saved character, unless the player already is it.</summary>
        private async Task<bool> EnsureWearingCharacter(string characterKey)
        {
            if (IsWearingCharacter(characterKey))
            {
                return true;
            }
            await SpawnThisCharacter(characterKey, true);
            if (IsWearingCharacter(characterKey))
            {
                return true;
            }
            Notify.Error("This character couldn't be spawned.");
            return false;
        }

        private static StyleLayer StyleLayerOf(PedAppearance appearance, int overlay) => overlay switch
        {
            1 => new StyleLayer { Style = appearance.beardStyle, Opacity = appearance.beardOpacity, Color = appearance.beardColor },
            2 => new StyleLayer { Style = appearance.eyebrowsStyle, Opacity = appearance.eyebrowsOpacity, Color = appearance.eyebrowsColor },
            4 => new StyleLayer { Style = appearance.makeupStyle, Opacity = appearance.makeupOpacity, Color = appearance.makeupColor },
            5 => new StyleLayer { Style = appearance.blushStyle, Opacity = appearance.blushOpacity, Color = appearance.blushColor },
            8 => new StyleLayer { Style = appearance.lipstickStyle, Opacity = appearance.lipstickOpacity, Color = appearance.lipstickColor },
            _ => null,
        };

        /// <summary>
        /// The player ped's hair and makeup now, as a style. The game can't report the hair's fade
        /// overlay, so it comes from <paramref name="character"/> (or the character being worn) when
        /// the haircut is theirs, and otherwise as the character creator picks it.
        /// </summary>
        private HairMakeupStyle CaptureHairMakeupStyle(MultiplayerPedData character)
        {
            var ped = Game.PlayerPed.Handle;
            var hair = GetPedDrawableVariation(ped, 2);
            var known = IsWearingCharacter(character.SaveName) ? currentCharacter.PedAppearance : character.PedAppearance;
            string[] fade = null;
            if (known.hairStyle == hair)
            {
                if (!string.IsNullOrEmpty(known.HairOverlay.Key) && !string.IsNullOrEmpty(known.HairOverlay.Value))
                {
                    fade = new[] { known.HairOverlay.Key, known.HairOverlay.Value };
                }
            }
            else if (hairOverlays.TryGetValue(hair, out var overlay))
            {
                fade = new[] { overlay.Key, overlay.Value };
            }
            return HairMakeupStyle.Capture((uint)GetEntityModel(ped), hair, GetPedHairColor(ped), GetPedHairHighlightColor(ped), fade, id =>
            {
                int value = 0, colourType = 0, firstColour = 0, secondColour = 0;
                var opacity = 0f;
                return GetPedHeadOverlayData(ped, id, ref value, ref colourType, ref firstColour, ref secondColour, ref opacity)
                    ? new StyleLayer { Style = value, Opacity = opacity, Color = firstColour }
                    : StyleLayerOf(known, id);
            });
        }

        /// <summary>The character with a style's hair and makeup; its face, clothes and the rest stay as they are.</summary>
        private static MultiplayerPedData WithHairMakeupStyle(MultiplayerPedData character, HairMakeupStyle style, bool includeHair)
        {
            var appearance = character.PedAppearance;
            if (includeHair)
            {
                appearance.hairStyle = style.Hair;
                appearance.HairOverlay = style.HairOverlay == null
                    ? new KeyValuePair<string, string>("", "")
                    : new KeyValuePair<string, string>(style.HairOverlay[0], style.HairOverlay[1]);
            }
            appearance.hairColor = style.HairColor;
            appearance.hairHighlightColor = style.HairHighlight;
            foreach (var layer in style.Overlays)
            {
                var l = layer.Value;
                switch (layer.Key)
                {
                    case 1:
                        appearance.beardStyle = l.Style;
                        appearance.beardOpacity = l.Opacity;
                        appearance.beardColor = l.Color;
                        break;
                    case 2:
                        appearance.eyebrowsStyle = l.Style;
                        appearance.eyebrowsOpacity = l.Opacity;
                        appearance.eyebrowsColor = l.Color;
                        break;
                    case 4:
                        appearance.makeupStyle = l.Style;
                        appearance.makeupOpacity = l.Opacity;
                        appearance.makeupColor = l.Color;
                        break;
                    case 5:
                        appearance.blushStyle = l.Style;
                        appearance.blushOpacity = l.Opacity;
                        appearance.blushColor = l.Color;
                        break;
                    case 8:
                        appearance.lipstickStyle = l.Style;
                        appearance.lipstickOpacity = l.Opacity;
                        appearance.lipstickColor = l.Color;
                        break;
                }
            }
            character.PedAppearance = appearance;
            return character;
        }

        /// <summary>
        /// Gives the player ped a style's hair and makeup, as the appearance menu does, and records it
        /// in the character being worn. <paramref name="problem"/> says what couldn't be applied.
        /// </summary>
        private bool ApplyHairMakeupStyle(HairMakeupStyle style, out string problem)
        {
            problem = null;
            var ped = Game.PlayerPed.Handle;
            if (style == null || style.Model != (uint)GetEntityModel(ped))
            {
                problem = "This style is for the other body type.";
                return false;
            }
            var hairFits = style.Hair < GetNumberOfPedDrawableVariations(ped, 2);
            if (hairFits)
            {
                ClearPedFacialDecorations(ped);
                SetPedComponentVariation(ped, 2, style.Hair, 0, 0);
                if (style.HairOverlay != null)
                {
                    SetPedFacialDecoration(ped, (uint)GetHashKey(style.HairOverlay[0]), (uint)GetHashKey(style.HairOverlay[1]));
                }
            }
            else
            {
                problem = "Its haircut isn't available on this server, so your haircut wasn't changed.";
            }
            SetPedHairColor(ped, style.HairColor, style.HairHighlight);
            foreach (var layer in style.Overlays)
            {
                SetPedHeadOverlay(ped, layer.Key, layer.Value.Style, layer.Value.Opacity);
                SetPedHeadOverlayColor(ped, layer.Key, HairMakeupStyle.ColorType(layer.Key), layer.Value.Color, layer.Value.Color);
            }
            if (currentCharacter.ModelHash == style.Model)
            {
                currentCharacter = WithHairMakeupStyle(currentCharacter, style, hairFits);
                RefreshCurrentCharacterSnapshot();
            }
            return true;
        }

        /// <summary>
        /// A saved character's own Outfits, or its Hair &amp; Makeup Styles (CharacterWardrobe.cs): save
        /// what you have now under a name; wear or apply, update, rename or delete one. Wearing or
        /// applying one while you're someone else spawns this character first.
        /// </summary>
        private void CreateCharacterWardrobeMenu(Menu wardrobeMenu, CharacterWardrobe.Kind kind)
        {
            var outfits = kind == CharacterWardrobe.Kind.Outfit;
            var what = outfits ? "outfit" : "style";
            var What = outfits ? "Outfit" : "Style";
            var saveCurrent = new MenuItem($"Save Current {What}", outfits
                ? "Saves the clothes and props you're wearing as one of this character's outfits."
                : "Saves your hair, hair colour and highlight, eyebrows, beard, makeup, blush and lipstick as one of this character's styles.");
            var keepLook = new MenuItem("Keep Current Hair & Makeup", "Makes the hair and makeup you have now this character's usual look, so it spawns with them. Only while you are this character.");

            string CharacterKey() => "mp_ped_" + selectedSavedCharacterManageName;
            string Who() => selectedSavedCharacterManageName;
            List<string> Names() => CharacterWardrobe.Names(kind, CharacterKey(), KvpKeys(CharacterWardrobe.KeyPrefix(kind, CharacterKey())));

            void Rebuild()
            {
                wardrobeMenu.MenuSubtitle = $"{Who()}: {(outfits ? "Outfits" : "Hair & Makeup Styles")}";
                wardrobeMenu.ClearMenuItems();
                wardrobeMenu.AddMenuItem(saveCurrent);
                if (!outfits)
                {
                    wardrobeMenu.AddMenuItem(keepLook);
                }
                var names = Names();
                if (names.Count == 0)
                {
                    wardrobeMenu.AddMenuItem(new MenuItem($"No {what}s yet", outfits
                        ? "Put on the clothes you want, then choose Save Current Outfit."
                        : "Style your hair and makeup, then choose Save Current Style.") { Enabled = false });
                }
                foreach (var name in names)
                {
                    wardrobeMenu.AddMenuItem(new MenuListItem(name, new List<string> { outfits ? "Wear" : "Apply", "Update", "Rename", "Delete" }, 0,
                        $"{(outfits ? "Wear changes into it" : "Apply gives you this hair and makeup")}, spawning this character first if you're someone else. Update replaces it with what you have now. Choose one, then press Enter.")
                    { ItemData = name });
                }
                wardrobeMenu.RefreshIndex();
            }

            bool LoadCharacter(out MultiplayerPedData character)
            {
                character = StorageManager.GetSavedMpCharacterData(CharacterKey());
                if (character.Version < 1 || character.ModelHash == 0)
                {
                    Notify.Error("This character couldn't be loaded.");
                    return false;
                }
                return true;
            }

            bool OnCharactersBody(MultiplayerPedData character)
            {
                if (IsCurrentPedMpFreemode() && (uint)GetEntityModel(Game.PlayerPed.Handle) == character.ModelHash)
                {
                    return true;
                }
                Notify.Error($"You're not using {Who()}'s body type. Spawn the character first.");
                return false;
            }

            string Capture(MultiplayerPedData character) => outfits ? CaptureCurrentOutfit().ToJson() : CaptureHairMakeupStyle(character).ToJson();

            wardrobeMenu.OnMenuOpen += (m) => Rebuild();

            wardrobeMenu.OnItemSelect += async (sender, item, index) =>
            {
                if (item == saveCurrent)
                {
                    if (!LoadCharacter(out var character) || !OnCharactersBody(character))
                    {
                        return;
                    }
                    var name = CharacterWardrobe.CleanName(await GetUserInput($"{What} name", CharacterWardrobe.MaxNameLength), out var error);
                    if (name == null)
                    {
                        Notify.Error(error);
                        return;
                    }
                    var key = CharacterWardrobe.KeyFor(kind, CharacterKey(), name);
                    if (!string.IsNullOrEmpty(GetResourceKvpString(key)))
                    {
                        Notify.Error($"{Who()} already has a {what} called {name}. Choose Update on it to replace it.");
                        return;
                    }
                    if (Names().Count >= CharacterWardrobe.MaxEntries)
                    {
                        Notify.Error($"A character can have up to {CharacterWardrobe.MaxEntries} {what}s. Delete one first.");
                        return;
                    }
                    var json = Capture(character);
                    SetResourceKvp(key, json);
                    if (GetResourceKvpString(key) != json)
                    {
                        Notify.Error($"The {what} couldn't be saved.");
                        return;
                    }
                    Notify.Success($"{What} {name} saved for {Who()}.");
                    Rebuild();
                }
                else if (item == keepLook)
                {
                    var characterKey = CharacterKey();
                    if (!LoadCharacter(out var character))
                    {
                        return;
                    }
                    if (!IsWearingCharacter(characterKey))
                    {
                        Notify.Error($"Spawn {Who()} first: this keeps the hair and makeup you have on now.");
                        return;
                    }
                    if (!await GetUserConfirmation("Keep Hair & Makeup", $"Make the hair and makeup you have now **{Who()}**'s usual look? The character's saved hair and makeup are replaced."))
                    {
                        return;
                    }
                    var style = CaptureHairMakeupStyle(character);
                    character = WithHairMakeupStyle(character, style, true);
                    if (!StorageManager.SaveJsonData(characterKey, JsonConvert.SerializeObject(character), true))
                    {
                        Notify.Error("The character couldn't be saved.");
                        return;
                    }
                    currentCharacter = WithHairMakeupStyle(currentCharacter, style, true);
                    RefreshCurrentCharacterSnapshot();
                    Notify.Success($"{Who()} now spawns with this hair and makeup.");
                }
            };

            wardrobeMenu.OnListItemSelect += async (sender, item, listIndex, itemIndex) =>
            {
                if (item.ItemData is not string name)
                {
                    return;
                }
                var characterKey = CharacterKey();
                var key = CharacterWardrobe.KeyFor(kind, characterKey, name);
                var json = GetResourceKvpString(key);
                if (string.IsNullOrEmpty(json))
                {
                    Notify.Error($"That {what} no longer exists.");
                    Rebuild();
                    return;
                }
                if (!LoadCharacter(out var character))
                {
                    return;
                }

                if (listIndex == 0 && outfits)
                {
                    var save = ClothingSaves.Read(ClothingSaves.Kind.Outfit, json);
                    if (save == null || save.Model != character.ModelHash)
                    {
                        Notify.Error("This outfit can't be read.");
                        return;
                    }
                    if (!await EnsureWearingCharacter(characterKey))
                    {
                        return;
                    }
                    if (!WearOutfit(save, $"{Who()}: {name}"))
                    {
                        Notify.Error("This outfit can't be worn by this character.");
                        return;
                    }
                    Notify.Success($"Changed into {name}. Choose Update Character Clothing to make it {Who()}'s usual look.");
                }
                else if (listIndex == 0)
                {
                    var style = HairMakeupStyle.Parse(json);
                    if (style == null || style.Model != character.ModelHash)
                    {
                        Notify.Error("This style can't be read.");
                        return;
                    }
                    if (!await EnsureWearingCharacter(characterKey))
                    {
                        return;
                    }
                    if (!ApplyHairMakeupStyle(style, out var problem))
                    {
                        Notify.Error(problem);
                        return;
                    }
                    if (problem != null)
                    {
                        Notify.Alert(problem);
                    }
                    Notify.Success($"Applied {name}. Choose Keep Current Hair & Makeup to make it {Who()}'s usual look.");
                }
                else if (listIndex == 1)
                {
                    if (!OnCharactersBody(character)
                        || !await GetUserConfirmation($"Update {What}", $"Replace the {what} **{name}** with what you have now?"))
                    {
                        return;
                    }
                    var updated = Capture(character);
                    SetResourceKvp(key, updated);
                    if (GetResourceKvpString(key) != updated)
                    {
                        Notify.Error($"The {what} couldn't be saved.");
                        return;
                    }
                    Notify.Success($"{What} {name} updated with what you have now.");
                }
                else if (listIndex == 2)
                {
                    var newName = CharacterWardrobe.CleanName(await GetUserInput($"Rename {name}", name, CharacterWardrobe.MaxNameLength), out var error);
                    if (newName == null)
                    {
                        Notify.Error(error);
                        return;
                    }
                    if (newName == name)
                    {
                        return;
                    }
                    var newKey = CharacterWardrobe.KeyFor(kind, characterKey, newName);
                    if (!string.IsNullOrEmpty(GetResourceKvpString(newKey)))
                    {
                        Notify.Error($"{Who()} already has a {what} called {newName}.");
                        return;
                    }
                    SetResourceKvp(newKey, json);
                    if (GetResourceKvpString(newKey) != json)
                    {
                        Notify.Error($"The {what} couldn't be renamed.");
                        return;
                    }
                    DeleteResourceKvp(key);
                    Notify.Success($"{What} {name} renamed to {newName}.");
                    Rebuild();
                }
                else if (listIndex == 3 && await GetUserConfirmation($"Delete {What}", $"Delete the {what} **{name}** of {Who()}?"))
                {
                    DeleteResourceKvp(key);
                    Notify.Success($"{What} {name} deleted.");
                    Rebuild();
                }
            };
        }

        /// <summary>
        /// The character with the clothes and props the player ped wears now. With
        /// <paramref name="keepUnresolved"/>, slots that couldn't be matched when the character was put
        /// on keep their saved entry (so a snapshot never turns an unmatched piece into whatever
        /// the ped shows there).
        /// </summary>
        private MultiplayerPedData ReplacePedDataClothing(MultiplayerPedData character, bool keepUnresolved = false)
        {
            int handle = Game.PlayerPed.Handle;
            if (character.ModelHash != 0 && (uint)GetEntityModel(handle) != character.ModelHash)
            {
                return character; // never record a look worn by another body
            }
            var saved = ClothingService.FromCharacter(character);
            var refs = saved.Refs?.Clone() ?? new ClothingRefSet { Model = character.ModelHash };
            refs.Model = character.ModelHash;
            var keep = new HashSet<ClothingSlot>();
            foreach (var slot in keepUnresolved ? unresolvedSlots : new HashSet<ClothingSlot>())
            {
                if (refs.Get(slot) == null && saved.GlobalsLayout == null && saved.TryGetGlobal(slot, out var oldDrawable, out var oldTexture))
                {
                    refs.Set(slot, ClothingRef.Pending(oldDrawable, oldTexture, "unmatched"));
                }
                if (refs.Get(slot) != null)
                {
                    keep.Add(slot);
                }
            }

            if (character.DrawableVariations.clothes == null)
            {
                character.DrawableVariations.clothes = new Dictionary<int, KeyValuePair<int, int>>();
            }

            // Head and hair are appearance data, not clothing.
            character.DrawableVariations.clothes.Remove(0);
            character.DrawableVariations.clothes.Remove(2);

            if (character.PropVariations.props == null)
            {
                character.PropVariations.props = new Dictionary<int, KeyValuePair<int, int>>();
            }

            // Drawables
            for (int i = 0; i < 12; i++)
            {
                if (i is 0 or 2)
                {
                    continue;
                }

                var slot = ClothingSlot.Component(i);
                if (keep.Contains(slot))
                {
                    continue;
                }
                int drawable = GetPedDrawableVariation(handle, i);
                int texture = GetPedTextureVariation(handle, i);
                character.DrawableVariations.clothes[i] = new KeyValuePair<int, int>(drawable, texture);
                refs.Set(slot, ClothingService.CaptureRef(handle, slot));
            }

            for (int i = 0; i < 8; i++)
            {
                var slot = ClothingSlot.PropSlot(i);
                if (keep.Contains(slot))
                {
                    continue;
                }
                int prop = GetPedPropIndex(handle, i);
                int texture = GetPedPropTextureIndex(handle, i);
                character.PropVariations.props[i] = new KeyValuePair<int, int>(prop, texture);
                refs.Set(slot, ClothingService.CaptureRef(handle, slot));
            }

            refs.Clothes.Remove(0);
            refs.Clothes.Remove(2);
            character.ClothingRefs = refs;
            // Numbers that aren't current (kept unmatched pieces) have their own entry, so the
            // cache is stamped with the running layout.
            character.ClothingLayout = ClothingService.LayoutPrint(handle);
            return character;
        }

        private async Task SpawnCharacter(MultiplayerPedData character, bool restoreWeapons)
        {
            currentCharacter = character;
            await SpawnSavedPed(restoreWeapons);
        }

        private static MultiplayerPedData GetLastMpCharacterSnapshot()
        {
            var output = new MultiplayerPedData();
            var json = GetResourceKvpString(LastMpCharacterSnapshotKey);
            if (string.IsNullOrEmpty(json))
            {
                return output;
            }

            try
            {
                output = JsonConvert.DeserializeObject<MultiplayerPedData>(json);
            }
            catch (JsonException e)
            {
                Debug.WriteLine(e.Message);
            }

            return output;
        }

        private static bool SaveMpCharacterSnapshot(MultiplayerPedData character)
        {
            if (character.Version < 1 || string.IsNullOrEmpty(character.SaveName))
            {
                return false;
            }

            var json = JsonConvert.SerializeObject(character);
            SetResourceKvp(LastMpCharacterSnapshotKey, json);
            return GetResourceKvpString(LastMpCharacterSnapshotKey) == json;
        }

        private static string GetMpCharacterKey(string saveName)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                return "";
            }

            return saveName.StartsWith("mp_ped_") ? saveName : "mp_ped_" + saveName;
        }

        private static string NormalizeMpCharacterKey(string saveName)
        {
            if (string.IsNullOrEmpty(saveName))
            {
                return "";
            }

            while (saveName.StartsWith("mp_ped_mp_ped_"))
            {
                saveName = saveName.Substring("mp_ped_".Length);
            }

            return GetMpCharacterKey(saveName);
        }

        private static bool IsCurrentPedMpFreemode()
        {
            var model = (uint)GetEntityModel(Game.PlayerPed.Handle);
            return model == (uint)GetHashKey("mp_m_freemode_01") || model == (uint)GetHashKey("mp_f_freemode_01");
        }


        internal void ChangePlayerHair(int newHairIndex)
        {
            ClearPedFacialDecorations(Game.PlayerPed.Handle);
            currentCharacter.PedAppearance.HairOverlay = new KeyValuePair<string, string>("", "");
            currentCharacter.DrawableVariations.clothes?.Remove(2);

            if (newHairIndex >= GetNumberOfPedDrawableVariations(Game.PlayerPed.Handle, 2))
            {
                SetPedComponentVariation(Game.PlayerPed.Handle, 2, 0, 0, 0);
                currentCharacter.PedAppearance.hairStyle = 0;
            }
            else
            {
                SetPedComponentVariation(Game.PlayerPed.Handle, 2, newHairIndex, 0, 0);
                currentCharacter.PedAppearance.hairStyle = newHairIndex;
                if (hairOverlays.ContainsKey(newHairIndex))
                {
                    SetPedFacialDecoration(Game.PlayerPed.Handle, (uint)GetHashKey(hairOverlays[newHairIndex].Key), (uint)GetHashKey(hairOverlays[newHairIndex].Value));
                    currentCharacter.PedAppearance.HairOverlay = new KeyValuePair<string, string>(hairOverlays[newHairIndex].Key, hairOverlays[newHairIndex].Value);
                }
            }

            _hairSelection = newHairIndex;
        }

        internal void ChangePlayerHairColor(int color, int highlight)
        {
            SetPedHairColor(Game.PlayerPed.Handle, color, highlight);

            currentCharacter.PedAppearance.hairColor = color;
            currentCharacter.PedAppearance.hairHighlightColor = highlight;

            _hairColorSelection = color;
            _hairHighlightColorSelection = highlight;
        }

        internal void ChangePlayerEyeColor(int color)
        {
            SetPedEyeColor(Game.PlayerPed.Handle, color);

            currentCharacter.PedAppearance.eyeColor = color;

            _eyeColorSelection = color;
        }

        internal void SetPlayerClothing()
        {
            SetPedComponentVariation(Game.PlayerPed.Handle, 3, 15, 0, 0);

            currentCharacter.DrawableVariations.clothes[3] = new KeyValuePair<int, int>(15, 0);

            if (currentCharacter.IsMale)
            {
                SetPedComponentVariation(Game.PlayerPed.Handle, 8, 15, 0, 0);

                currentCharacter.DrawableVariations.clothes[8] = new KeyValuePair<int, int>(15, 0);

                SetPedComponentVariation(Game.PlayerPed.Handle, 11, 15, 0, 0);

                currentCharacter.DrawableVariations.clothes[11] = new KeyValuePair<int, int>(15, 0);

                int pantsColor = _random.Next(GetNumberOfPedTextureVariations(Game.PlayerPed.Handle, 4, 61));

                SetPedComponentVariation(Game.PlayerPed.Handle, 4, 61, pantsColor, 0);

                currentCharacter.DrawableVariations.clothes[4] = new KeyValuePair<int, int>(61, pantsColor);

                SetPedComponentVariation(Game.PlayerPed.Handle, 6, 34, 0, 0);

                currentCharacter.DrawableVariations.clothes[6] = new KeyValuePair<int, int>(34, 0);
            }
            else
            {
                SetPedComponentVariation(Game.PlayerPed.Handle, 8, 14, 0, 0);
                SetPedComponentVariation(Game.PlayerPed.Handle, 8, 14, 0, 0);

                currentCharacter.DrawableVariations.clothes[8] = new KeyValuePair<int, int>(14, 0);

                int braColor = _random.Next(GetNumberOfPedTextureVariations(Game.PlayerPed.Handle, 4, 17));

                SetPedComponentVariation(Game.PlayerPed.Handle, 4, 17, braColor, 0);

                currentCharacter.DrawableVariations.clothes[4] = new KeyValuePair<int, int>(17, braColor);

                SetPedComponentVariation(Game.PlayerPed.Handle, 11, 18, braColor, 0);

                currentCharacter.DrawableVariations.clothes[11] = new KeyValuePair<int, int>(18, braColor);

                SetPedComponentVariation(Game.PlayerPed.Handle, 6, 35, 0, 0);

                currentCharacter.DrawableVariations.clothes[6] = new KeyValuePair<int, int>(35, 0);
            }
        }

        /// <summary>
        /// Sets all the ped's overlay colors to their default (0) entry.
        /// When called, prevents default color being bright green.
        /// </summary>
        internal void DefaultPlayerColors()
        {
            faceSelection.Reset(IsPedMale(Game.PlayerPed.Handle));

            for (int i = 0; i < 12; i++)
            {
                int color = 0;
                int colorIndex = 0;

                switch (i)
                {
                    case 1:
                        colorIndex = 1;
                        break;

                    case 2:
                        colorIndex = 1;
                        break;

                    case 8:
                        colorIndex = 2;
                        break;

                    case 10:
                        colorIndex = 1;
                        break;

                    default:
                        continue;
                }

                SetPedHeadOverlay(Game.PlayerPed.Handle, i, 0, 0f);

                if (colorIndex > 0)
                {
                    SetPedHeadOverlayColor(Game.PlayerPed.Handle, i, colorIndex, color, color);
                }
            }
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

        /// <summary>
        /// Dresses a ped as a saved character. Returns the character with its clothing references
        /// current (and the global numbers cache updated for what was put on).
        /// </summary>
        internal async Task<MultiplayerPedData> AppySavedDataToPed(MultiplayerPedData character, int pedHandle, bool notifyClothingIssues = true)
        {
            #region headblend
            PedHeadBlendData data = character.PedHeadBlendData;
            SetPedHeadBlendData(pedHandle, data.FirstFaceShape, data.SecondFaceShape, data.ThirdFaceShape, data.FirstSkinTone, data.SecondSkinTone, data.ThirdSkinTone, data.ParentFaceShapePercent, data.ParentSkinTonePercent, data.ParentThirdUnkPercent, data.IsParentInheritance);

            while (!HasPedHeadBlendFinished(pedHandle))
            {
                await Delay(0);
            }
            #endregion

            #region appearance
            PedAppearance appData = character.PedAppearance;
            // hair
            SetPedComponentVariation(pedHandle, 2, appData.hairStyle, 0, 0);
            SetPedHairColor(pedHandle, appData.hairColor, appData.hairHighlightColor);
            if (!string.IsNullOrEmpty(appData.HairOverlay.Key) && !string.IsNullOrEmpty(appData.HairOverlay.Value))
            {
                SetPedFacialDecoration(pedHandle, (uint)GetHashKey(appData.HairOverlay.Key), (uint)GetHashKey(appData.HairOverlay.Value));
            }
            // blemishes
            SetPedHeadOverlay(pedHandle, 0, appData.blemishesStyle, appData.blemishesOpacity);
            // bread
            SetPedHeadOverlay(pedHandle, 1, appData.beardStyle, appData.beardOpacity);
            SetPedHeadOverlayColor(pedHandle, 1, 1, appData.beardColor, appData.beardColor);
            // eyebrows
            SetPedHeadOverlay(pedHandle, 2, appData.eyebrowsStyle, appData.eyebrowsOpacity);
            SetPedHeadOverlayColor(pedHandle, 2, 1, appData.eyebrowsColor, appData.eyebrowsColor);
            // ageing
            SetPedHeadOverlay(pedHandle, 3, appData.ageingStyle, appData.ageingOpacity);
            // makeup
            SetPedHeadOverlay(pedHandle, 4, appData.makeupStyle, appData.makeupOpacity);
            SetPedHeadOverlayColor(pedHandle, 4, 2, appData.makeupColor, appData.makeupColor);
            // blush
            SetPedHeadOverlay(pedHandle, 5, appData.blushStyle, appData.blushOpacity);
            SetPedHeadOverlayColor(pedHandle, 5, 2, appData.blushColor, appData.blushColor);
            // complexion
            SetPedHeadOverlay(pedHandle, 6, appData.complexionStyle, appData.complexionOpacity);
            // sundamage
            SetPedHeadOverlay(pedHandle, 7, appData.sunDamageStyle, appData.sunDamageOpacity);
            // lipstick
            SetPedHeadOverlay(pedHandle, 8, appData.lipstickStyle, appData.lipstickOpacity);
            SetPedHeadOverlayColor(pedHandle, 8, 2, appData.lipstickColor, appData.lipstickColor);
            // moles and freckles
            SetPedHeadOverlay(pedHandle, 9, appData.molesFrecklesStyle, appData.molesFrecklesOpacity);
            // chest hair 
            SetPedHeadOverlay(pedHandle, 10, appData.chestHairStyle, appData.chestHairOpacity);
            SetPedHeadOverlayColor(pedHandle, 10, 1, appData.chestHairColor, appData.chestHairColor);
            // body blemishes 
            SetPedHeadOverlay(pedHandle, 11, appData.bodyBlemishesStyle, appData.bodyBlemishesOpacity);
            // eyecolor
            SetPedEyeColor(pedHandle, appData.eyeColor);
            #endregion

            #region Face Shape Data
            for (var i = 0; i < 19; i++)
            {
                SetPedFaceFeature(pedHandle, i, 0f);
            }

            if (character.FaceShapeFeatures.features != null)
            {
                foreach (var t in character.FaceShapeFeatures.features)
                {
                    SetPedFaceFeature(pedHandle, t.Key, t.Value);
                }
            }
            else
            {
                character.FaceShapeFeatures.features = new Dictionary<int, float>();
            }

            #endregion

            #region Clothing and Props Data
            // Through the shared clothing resolver: collection references, or old numbers read with
            // the captured baseline. Pieces that can't be matched are left off; the save keeps them.
            var clothingSave = ClothingService.FromCharacter(character);
            var resolution = ClothingService.Resolve(clothingSave, ClothingSaves.CharacterSlots(clothingSave), pedHandle);
            ClothingService.Apply(resolution, pedHandle, 0);
            character = ClothingService.WithResolution(character, resolution);
            if (pedHandle == Game.PlayerPed.Handle)
            {
                unresolvedSlots = new HashSet<ClothingSlot>(resolution.Issues.Select(i => i.Slot));
            }
            ClothingService.Report(string.IsNullOrEmpty(character.SaveName) ? "Character" : character.SaveName.Replace("mp_ped_", ""), resolution, notifyClothingIssues);
            #endregion

            #region Tattoos

            if (character.PedTatttoos.HeadTattoos == null)
            {
                character.PedTatttoos.HeadTattoos = new List<KeyValuePair<string, string>>();
            }
            if (character.PedTatttoos.TorsoTattoos == null)
            {
                character.PedTatttoos.TorsoTattoos = new List<KeyValuePair<string, string>>();
            }
            if (character.PedTatttoos.LeftArmTattoos == null)
            {
                character.PedTatttoos.LeftArmTattoos = new List<KeyValuePair<string, string>>();
            }
            if (character.PedTatttoos.RightArmTattoos == null)
            {
                character.PedTatttoos.RightArmTattoos = new List<KeyValuePair<string, string>>();
            }
            if (character.PedTatttoos.LeftLegTattoos == null)
            {
                character.PedTatttoos.LeftLegTattoos = new List<KeyValuePair<string, string>>();
            }
            if (character.PedTatttoos.RightLegTattoos == null)
            {
                character.PedTatttoos.RightLegTattoos = new List<KeyValuePair<string, string>>();
            }
            if (character.PedTatttoos.BadgeTattoos == null)
            {
                character.PedTatttoos.BadgeTattoos = new List<KeyValuePair<string, string>>();
            }

            foreach (var tattoo in character.PedTatttoos.HeadTattoos)
            {
                SetPedDecoration(pedHandle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
            }
            foreach (var tattoo in character.PedTatttoos.TorsoTattoos)
            {
                SetPedDecoration(pedHandle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
            }
            foreach (var tattoo in character.PedTatttoos.LeftArmTattoos)
            {
                SetPedDecoration(pedHandle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
            }
            foreach (var tattoo in character.PedTatttoos.RightArmTattoos)
            {
                SetPedDecoration(pedHandle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
            }
            foreach (var tattoo in character.PedTatttoos.LeftLegTattoos)
            {
                SetPedDecoration(pedHandle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
            }
            foreach (var tattoo in character.PedTatttoos.RightLegTattoos)
            {
                SetPedDecoration(pedHandle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
            }
            foreach (var tattoo in character.PedTatttoos.BadgeTattoos)
            {
                SetPedDecoration(pedHandle, TattoosData.HashOf(tattoo.Key), TattoosData.HashOf(tattoo.Value));
            }
            #endregion

            return character;
        }

        public struct MpCharacterCategory
        {
            public string Name;
            public string Description;
            public MenuItem.Icon Icon;
        }
    }
}
