using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace LastArbiterAndroid
{
    // Android port of LastArbiter: retunes the Last Judge boss FSM and raises its HP.
    // Differences from the PC mod:
    //  - new Harmony() + raw MethodInfo postfix (no CreateAndPatchAll / HarmonyMethod)
    //  - every state is edited independently and null-safely: a missing state or action is
    //    logged and skipped instead of aborting the rest (the original would throw a
    //    NullReferenceException, which this bridge swallows silently)
    //  - Awake and the postfix are wrapped in try/catch
    // Change the GUID/name below to your own.
    [BepInPlugin("com.yourname.lastarbiterandroid", "Last Arbiter Android", "1.0.0")]
    public class LastArbiterPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static bool hookLogged;

        private void Awake()
        {
            Log = Logger;
            try
            {
                var original = AccessTools.Method(typeof(PlayMakerFSM), "Awake");
                if (original == null)
                {
                    Log.LogError("PlayMakerFSM.Awake not found");
                    return;
                }

                var postfix = AccessTools.Method(typeof(LastArbiterPlugin), nameof(Postfix));

                var harmony = new Harmony();                                   // parameterless
                harmony.Patch(original, prefix: null, postfix: postfix);       // raw MethodInfo, no HarmonyMethod
                Log.LogInfo("Patched PlayMakerFSM.Awake");
            }
            catch (Exception ex)
            {
                Log.LogError("Awake failed: " + ex);
            }
        }

        // Runs after every PlayMakerFSM.Awake in the game; only the Last Judge "Control" FSM is touched.
        private static void Postfix(PlayMakerFSM __instance)
        {
            try
            {
                if (!hookLogged)
                {
                    hookLogged = true;
                    Log.LogInfo("PlayMakerFSM.Awake hook is firing (__instance " +
                                (__instance == null ? "is NULL" : "ok") + ")");
                }

                if (__instance == null) return;

                GameObject go = __instance.gameObject;
                if (go == null) return;
                if (!go.name.StartsWith("Last Judge", StringComparison.OrdinalIgnoreCase)) return;
                if (!string.Equals(__instance.FsmName, "Control", StringComparison.OrdinalIgnoreCase)) return;

                Log.LogInfo("Found Last Judge Control FSM, applying edits");
                Apply(__instance);
            }
            catch (Exception ex)
            {
                Log.LogError("Postfix failed: " + ex);
            }
        }

        // ---------- helpers ----------

        private static string N(NamedVariable v)
        {
            return v == null ? null : v.Name;
        }

        // Look up a state and run the edits on it; a missing state or an exception only skips that state.
        private static void Edit(PlayMakerFSM fsm, string stateName, Action<FsmState> edit)
        {
            FsmState state = fsm.Fsm.GetState(stateName);
            if (state == null)
            {
                Log.LogWarning("No state named " + stateName);
                return;
            }
            try
            {
                edit(state);
            }
            catch (Exception ex)
            {
                Log.LogError("Editing state '" + stateName + "' failed: " + ex);
            }
        }

        // Edit the first action of type T (optionally matching pred) in the state.
        private static void With<T>(FsmState state, Func<T, bool> pred, Action<T> edit) where T : FsmStateAction
        {
            foreach (FsmStateAction action in state.Actions)
            {
                T typed = action as T;
                if (typed == null) continue;
                if (pred != null && !pred(typed)) continue;
                edit(typed);
                return;
            }
            Log.LogWarning("State '" + state.Name + "': no matching " + typeof(T).Name);
        }

        // ---------- the actual boss tweaks (values from the original mod) ----------

        private static void Apply(PlayMakerFSM fsm)
        {
            Edit(fsm, "Idle", s =>
                With<Wait>(s, a => N(a.time) == "Idle Time", a => a.time = 0f));

            Edit(fsm, "Init", s =>
            {
                With<MultiplyIntByFloat>(s, a => N(a.storeResult) == "HP P2", a => a.multiplyFloat = 0.85f);
                With<MultiplyIntByFloat>(s, a => N(a.storeResult) == "HP P3", a => a.multiplyFloat = 0.25f);
            });

            Edit(fsm, "Intro Roar", s =>
                With<DisplayBossTitle>(s, null, a => a.bossTitle = "LAST_JUDGE"));

            Edit(fsm, "Spinning", s =>
                With<Wait>(s, null, a => a.time = 0.5f));

            Edit(fsm, "Rerage?", s =>
                With<IntCompare>(s, null, a => a.integer2 = -16));

            Edit(fsm, "Rage End", s =>
                With<SetFloatValue>(s, null, a => a.floatValue = 0.2f));

            Edit(fsm, "Flame Spin Antic L", s =>
                With<Wait>(s, null, a => a.time = 0f));

            Edit(fsm, "Flame Dir", s =>
                With<RandomFloat>(s, a => N(a.storeResult) == "Speed R", a =>
                {
                    a.min = 5f;
                    a.max = 15f;
                }));

            Edit(fsm, "L Flame R", s =>
            {
                With<Wait>(s, a => a.time.Value == 1.7f, a => a.time = 0.7f);
                With<WaitBool>(s, null, a => a.time = 0.5f);
                With<SendEventToRegisterDelay>(s, null, a => a.delay = 0.5f);
                With<Wait>(s, a => a.time.Value == 1.25f, a => a.time = 0.5f);
            });

            // NOTE: the original mod re-applied the "L Flame R" values here by mistake (copy/paste),
            // so the SendEventToRegisterDelay and the 1.25s Wait in this state were never changed.
            // Ported faithfully; uncomment the two lines below if you want them tuned too.
            Edit(fsm, "R Flame L", s =>
            {
                With<Wait>(s, a => a.time.Value == 1.7f, a => a.time = 0.7f);
                With<WaitBool>(s, null, a => a.time = 0.5f);
                // With<SendEventToRegisterDelay>(s, null, a => a.delay = 0.5f);
                // With<Wait>(s, a => a.time.Value == 1.25f, a => a.time = 0.5f);
            });

            Edit(fsm, "Charge", s =>
                With<Wait>(s, null, a => a.time = 1f));

            Edit(fsm, "Charge Flame", s =>
                With<Wait>(s, null, a => a.time = 0.2f));

            Edit(fsm, "Charge Antic 2", s =>
                With<Wait>(s, null, a => a.time = 0f));

            Edit(fsm, "Charge Start", s =>
                With<SetVelocityByScale>(s, null, a => a.speed = -22f));

            Edit(fsm, "Stomp Flame Antic", s =>
                With<Wait>(s, null, a => a.time = 0f));

            Edit(fsm, "Stomp Flames", s =>
                With<Wait>(s, null, a => a.time = 0.1f));

            Edit(fsm, "Stomp Regular", s =>
                With<Wait>(s, null, a => a.time = 0.1f));

            Edit(fsm, "Stomp Rise and Antic", s =>
            {
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween X", a => ((EaseFsmAction)a).time = 0.2f);
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween Y", a => ((EaseFsmAction)a).time = 0.1f);
                With<ActivateGameObjectDelay>(s, a => N(a.activate) == "Flamed Up", a => a.delay = 0.1f);
            });

            Edit(fsm, "Jump Rise", s =>
            {
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween X", a => ((EaseFsmAction)a).time = 0.2f);
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween Y", a => ((EaseFsmAction)a).time = 0.15f);
            });

            Edit(fsm, "Jump Fall", s =>
            {
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween X", a => ((EaseFsmAction)a).time = 0.15f);
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween Y", a => ((EaseFsmAction)a).time = 0.2f);
            });

            Edit(fsm, "Dash", s =>
                With<SetVelocityByScale>(s, null, a => a.speed = -40f));

            Edit(fsm, "Clamp L", s =>
                With<FloatClamp>(s, a => N(a.floatVariable) == "X Distance", a =>
                {
                    a.minValue = -24f;
                    a.maxValue = -8f;
                }));

            Edit(fsm, "Clamp R", s =>
                With<FloatClamp>(s, a => N(a.floatVariable) == "X Distance", a =>
                {
                    a.minValue = 8f;
                    a.maxValue = 24f;
                }));

            Edit(fsm, "Throw Rise", s =>
            {
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween X", a => ((EaseFsmAction)a).time = 0.1f);
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween Y", a => ((EaseFsmAction)a).time = 0.1f);
                With<Wait>(s, null, a => a.time = 0f);
            });

            Edit(fsm, "Throw Fall", s =>
            {
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween X", a => ((EaseFsmAction)a).time = 0.1f);
                With<EaseFloat>(s, a => N(a.floatVariable) == "Tween Y", a => ((EaseFsmAction)a).time = 0.1f);
            });

            Edit(fsm, "Slam Type", s =>
                With<ConvertBoolToFloat>(s, null, a => a.falseValue = 0f));

            Edit(fsm, "Stun Start", s =>
                With<SetFloatValue>(s, a => N(a.floatVariable) == "Stun Timer", a => a.floatValue = 1f));

            Edit(fsm, "Close Range", s =>
                With<SendRandomEventV4>(s, null, a =>
                {
                    a.weights = new FsmFloat[] { 0.25f, 0.4f, 0.35f };
                    a.eventMax = new FsmInt[] { 2, 2, 2 };
                    a.missedMax = new FsmInt[] { 4, 3, 3 };
                }));

            Edit(fsm, "Far Range", s =>
                With<SendRandomEventV4>(s, null, a =>
                {
                    a.weights = new FsmFloat[] { 0.3f, 0.3f, 0.2f, 0.2f };
                    a.eventMax = new FsmInt[] { 1, 1, 2, 2 };
                    a.missedMax = new FsmInt[] { 2, 2, 3, 2 };
                }));

            Edit(fsm, "Close Range F", s =>
                With<SendRandomEventV3>(s, null, a =>
                {
                    a.weights = new FsmFloat[] { 0.15f, 0.25f, 0.1f, 0.25f, 0.25f };
                    a.eventMax = new FsmInt[] { 1, 3, 1, 3, 1 };
                    a.missedMax = new FsmInt[] { 4, 2, 999, 4, 4 };
                }));

            Edit(fsm, "Far Range F", s =>
                With<SendRandomEventV3>(s, null, a =>
                {
                    a.weights = new FsmFloat[] { 0.2f, 0.2f, 0.2f, 0.1f, 0.3f };
                    a.eventMax = new FsmInt[] { 1, 2, 1, 1, 3 };
                    a.missedMax = new FsmInt[] { 2, 3, 999, 4, 2 };
                }));

            // Boss HP
            try
            {
                HealthManager hm = fsm.gameObject.GetComponent<HealthManager>();
                if (hm != null)
                {
                    hm.hp = 1440;
                }
                else
                {
                    Log.LogWarning("No HealthManager on Last Judge object");
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Setting HP failed: " + ex);
            }

            Log.LogInfo("Last Judge edits applied");
        }
    }
}
