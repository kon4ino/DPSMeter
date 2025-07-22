using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using IL;
using InControl;
using Modding;
using Modding.Converters;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Newtonsoft.Json;
using On;
using On.HutongGames.PlayMaker;
using Satchel;
using Satchel.BetterMenus;
using Satchel.Futils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

// mod by ino_ (kon4ino), 1.0.0

namespace DPSMeter
{

    public class KeyBinds : PlayerActionSet
    {
        public PlayerAction ToggleDisplay;

        public KeyBinds()
        {
            ToggleDisplay = CreatePlayerAction("Toggle Display");
            ToggleDisplay.AddDefaultBinding(Key.None);
        }
    }

    public class GlobalSettings
    {
        public bool EnableDPSMeter = true;
        public bool EnableDisplay = false;
        [JsonConverter(typeof(PlayerActionSetConverter))]
        public KeyBinds keybinds = new KeyBinds();
    }

    public class DPSMeter : Mod, ICustomMenuMod, IGlobalSettings<GlobalSettings>
    {
        public DPSMeter() : base("DPSMeter") { }
        public override string GetVersion() => "1.0.0";
        public static GlobalSettings GS = new GlobalSettings();
        private Menu menuRef;
        private Dictionary<GameObject, int> enemyHealth = new();
        private float lastDPSUpdateTime = 0f;
        public float DPS = 0f;
        public float maxDPS = 0f;
        public float minDPS = 0f;
        private float currentDPS = 0f;
        private float dpsUpdateInterval = 0.5f;
        private float dpsAccumulator = 0f;
        public bool ToggleButtonInsideMenu => true;

        public void OnLoadGlobal(GlobalSettings s)
        {
            GS = s;
        }
        public GlobalSettings OnSaveGlobal()
        {
            return GS;
        }

        public void EnableDPSMeter()
        {
            ModHooks.OnEnableEnemyHook += OnEnableEnemy;
            ModHooks.OnReceiveDeathEventHook += OnReceiveDeathEvent;
            ModHooks.HeroUpdateHook += OnHeroUpdate;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
            ModHooks.BeforeSavegameSaveHook += OnSaveSaved;
        }
        public void DisableDPSMeter()
        {
            ModDisplay.Instance?.Destroy();
            ModHooks.OnEnableEnemyHook -= OnEnableEnemy;
            ModHooks.OnReceiveDeathEventHook -= OnReceiveDeathEvent;
            ModHooks.HeroUpdateHook -= OnHeroUpdate;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneChanged;
            ModHooks.BeforeSavegameSaveHook -= OnSaveSaved;
        }

        private string BuildDisplayText()
        {
            return $"DPS: {DPS.ToString("F2")}";
        }

        public MenuScreen GetMenuScreen(MenuScreen modListMenu, ModToggleDelegates? toggleDelegates)
        {
            if (menuRef == null)
            {
                menuRef = new Menu("DPSMeter", new Element[]
                {

                    new HorizontalOption // ВКЛ/ВЫКЛ МОД
                    (
                        name: "Enable Mod",
                        description: "Toggle mod On/Off",
                        values: new[] { "Off", "On" },
                        applySetting: index =>
                        {
                            GS.EnableDPSMeter = index == 1;
                            OnSaveGlobal();
                            if (!GS.EnableDPSMeter)
                                DisableDPSMeter();
                            else
                                EnableDPSMeter();
                        },

                        loadSetting: () => GS.EnableDPSMeter ? 1 : 0
                    ),

                    new HorizontalOption // ВКЛ/ВЫКЛ ДИСПЛЕЙ
                    (
                        name: "Show DPS",
                        description: "Toggle DPS display On/Off",
                        values: new[] { "Off", "On" },
                        applySetting: index =>
                        {
                            GS.EnableDisplay = index == 1;
                            OnSaveGlobal();
                        },

                        loadSetting: () => GS.EnableDisplay ? 1 : 0
                    ),

                    new KeyBind
                    (
                        name: "Show/Hide Display Key",
                        playerAction: GS.keybinds.ToggleDisplay
                    ),

                    new MenuButton
                    (
                        name: "Reset Defaults",
                        description: "",
                        submitAction: (_) =>
                        {
                            if (GS.EnableDPSMeter)
                            {
                                GS.keybinds.ToggleDisplay.ClearBindings();

                                OnSaveGlobal();
                                menuRef?.Update();
                            }
                        }
                    )

                });
            }

            return menuRef.GetMenuScreen(modListMenu);
        }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            Log("Initializing");

            base.Initialize(preloadedObjects);
            ModHooks.OnEnableEnemyHook += OnEnableEnemy;
            ModHooks.OnReceiveDeathEventHook += OnReceiveDeathEvent;
            ModHooks.HeroUpdateHook += OnHeroUpdate;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
            ModHooks.BeforeSavegameSaveHook += OnSaveSaved;

            Log("Initialized");
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            // ОЧИЩАЕМ СЛОВАРИ
            enemyHealth.Clear();
            if (to.name == "Menu_Title" || to.name == "Quit_To_Menu")
            {
                ModDisplay.Instance?.Destroy();
            }
        }

        private void OnReceiveDeathEvent(EnemyDeathEffects enemyDeathEffects, bool eventAlreadyReceived, ref float? attackDirection, ref bool resetDeathEvent, ref bool spellBurn, ref bool isWatery)
        {
            if (!GS.EnableDPSMeter) return;
            if (enemyDeathEffects == null) return;

            GameObject corpse = enemyDeathEffects.gameObject;
            enemyHealth.Remove(corpse);
        }

        private bool OnEnableEnemy(GameObject enemy, bool isDead)
        {
            if (!GS.EnableDPSMeter) return isDead;
            if (enemy == null || isDead) return isDead;

            var hm = enemy.GetComponent<HealthManager>();
            if (hm != null && !enemyHealth.ContainsKey(enemy))
            {
                enemyHealth[enemy] = hm.hp;
            }

            return isDead;
        }

        private void OnSaveSaved(SaveGameData data)
        {
            lastDPSUpdateTime = 0f;
            DPS = 0f;
            maxDPS = 0f;
            minDPS = 0f;
            currentDPS = 0f;
            dpsAccumulator = 0f;
        }

        private void OnHeroUpdate()
        {
            if (!GS.EnableDPSMeter)
            {
                ModDisplay.Instance?.Destroy();
                return;
            }

            if (GS.keybinds.ToggleDisplay.WasPressed)
            {
                GS.EnableDisplay = !GS.EnableDisplay;
                OnSaveGlobal();

                if (!GS.EnableDisplay)
                    ModDisplay.Instance?.Destroy();
                else
                {
                    if (ModDisplay.Instance == null)
                        ModDisplay.Instance = new ModDisplay();
                    ModDisplay.Instance.Display(BuildDisplayText());
                }
            }

            if (GS.EnableDisplay)
            {
                if (ModDisplay.Instance == null)
                    ModDisplay.Instance = new ModDisplay();
                ModDisplay.Instance.Display(BuildDisplayText());
            }
            else
                ModDisplay.Instance?.Destroy();

            float currentTime = Time.time;
            int totalDamageThisFrame = 0;

            foreach (var kvp in enemyHealth.ToList())
            {
                GameObject enemy = kvp.Key;
                int oldHp = kvp.Value;

                if (enemy == null)
                {
                    enemyHealth.Remove(enemy);
                    continue;
                }

                var hm = enemy.GetComponent<HealthManager>();
                if (hm == null)
                {
                    enemyHealth.Remove(enemy);
                    continue;
                }

                int newHp = hm.hp;
                int damage = oldHp - newHp;

                totalDamageThisFrame += damage;
                enemyHealth[enemy] = newHp;
            }

            if (totalDamageThisFrame != 0)
            {
                dpsAccumulator += totalDamageThisFrame;
            }

            if (currentTime - lastDPSUpdateTime >= dpsUpdateInterval)
            {
                currentDPS = dpsAccumulator / dpsUpdateInterval;
                lastDPSUpdateTime = currentTime;

                if (currentDPS == 0)
                {
                    if (DPS == 0)
                        DPS = 0;
                    else
                        DPS *= 0.75f;
                }
                else
                {
                    DPS = currentDPS;
                }

                dpsAccumulator *= 0.75f;

                if (dpsAccumulator == 0f)
                    dpsAccumulator = 0f;
            }
        }
    }
}