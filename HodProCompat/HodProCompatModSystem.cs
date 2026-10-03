using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace HodProCompat
{
    /// <summary>
    /// Keeps Hydrate or Diedrate's nutrition deficit bar scaled to the real saturation bar.
    /// Prosequor changes max saturation, but HoD's bar was built around the vanilla scale,
    /// so its fill and 100-point partitions stop lining up with the saturation bar beneath it.
    /// Each tick we copy the saturation bar's min/max onto the HoD bar and convert its divider
    /// spacing to the way HoD draws dividers.
    /// </summary>
    public class HodProCompatModSystem : ModSystem
    {
        private const string HodHudTypeName = "HudElementNutritionDeficitBar";
        private const string VanillaHudTypeName = "HudStatbar";
        private const float FallbackLineInterval = 100f;
        private const int SyncIntervalMs = 100;

        private ICoreClientAPI capi;

        // HoD
        private GuiDialog hodHud;
        private FieldInfo hodStatbarField;
        private object hodStatbar;
        private MethodInfo hodSetLineInterval;
        private MethodInfo hodSetMinMax;

        // Vanilla saturation bar internals (private fields)
        private static readonly FieldInfo VanillaMin = Field(typeof(GuiElementStatbar), "minValue");
        private static readonly FieldInfo VanillaMax = Field(typeof(GuiElementStatbar), "maxValue");
        private static readonly FieldInfo VanillaInterval = Field(typeof(GuiElementStatbar), "lineInterval");

        private object lastHodStatbar;
        private float lastMin = float.NaN, lastMax = float.NaN, lastInterval = float.NaN;
        private bool loggedMissing;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override double ExecuteOrder() => 1.0;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Event.RegisterGameTickListener(Sync, SyncIntervalMs);
        }

        private static FieldInfo Field(Type type, string name) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private void Sync(float dt)
        {
            if (capi.World?.Player?.Entity == null) return;
            if (!ResolveHodBar()) return;

            GetSaturationScale(out float min, out float max, out float interval);
            if (!(max > 0f)) return;

            if (!ReferenceEquals(hodStatbar, lastHodStatbar))
            {
                // HoD recomposed its HUD and made a fresh bar; reapply everything.
                lastHodStatbar = hodStatbar;
                lastMin = lastMax = lastInterval = float.NaN;
            }

            if (min == lastMin && max == lastMax && interval == lastInterval) return;

            // Interval first: SetCustomMinMax always recomposes the overlay and picks it up.
            hodSetLineInterval.Invoke(hodStatbar, new object[] { ToHodInterval(min, max, interval) });
            hodSetMinMax.Invoke(hodStatbar, new object[] { min, max });

            capi.Logger.Debug("[HodProCompat] Deficit bar scale set to {0}-{1}, vanilla interval {2}", min, max, interval);
            lastMin = min;
            lastMax = max;
            lastInterval = interval;
        }

        /// <summary>
        /// Vanilla draws lines = min(50, span / interval) dividers at i / lines of the bar width, i = 1..lines-1.
        /// HoD instead draws a divider at every k * interval value units for k = 1..(int)(span / interval),
        /// including the bar's end. Returns the HoD interval that produces vanilla's divider positions:
        /// span / lines, nudged up so HoD's count comes out one lower (no divider at the end).
        /// </summary>
        private static float ToHodInterval(float min, float max, float vanillaInterval)
        {
            float span = max - min;
            int lines = vanillaInterval > 0f ? Math.Min(50, (int)(span / vanillaInterval)) : 0;
            if (lines < 2)
            {
                // Vanilla draws no dividers; HoD always draws at least one, so push it past the bar's end.
                return span * 10f;
            }
            return span / lines * 1.0001f;
        }

        /// <summary>Prefers the live saturation bar's own scale; falls back to the hunger tree.</summary>
        private void GetSaturationScale(out float min, out float max, out float interval)
        {
            min = 0f;
            max = 0f;
            interval = FallbackLineInterval;

            GuiElementStatbar bar = FindSaturationBar();
            if (bar != null && VanillaMax != null)
            {
                max = (float)VanillaMax.GetValue(bar);
                if (VanillaMin != null) min = (float)VanillaMin.GetValue(bar);
                if (VanillaInterval != null) interval = (float)VanillaInterval.GetValue(bar);
                if (max > min) return;
            }

            ITreeAttribute hunger = capi.World.Player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
            max = hunger?.TryGetFloat("maxsaturation") ?? 0f;
            min = 0f;
            interval = FallbackLineInterval;
        }

        private GuiElementStatbar FindSaturationBar()
        {
            foreach (GuiDialog gui in capi.Gui.LoadedGuis)
            {
                if (gui.GetType().Name != VanillaHudTypeName) continue;
                return gui.Composers["statbar"]?.GetStatbar("saturationstatbar");
            }
            return null;
        }

        private bool ResolveHodBar()
        {
            // Re-resolve every call: HoD rebuilds its HUD when player data arrives.
            hodHud = null;
            foreach (GuiDialog gui in capi.Gui.LoadedGuis)
            {
                if (gui.GetType().Name == HodHudTypeName) { hodHud = gui; break; }
            }

            if (hodHud == null)
            {
                if (!loggedMissing)
                {
                    capi.Logger.Warning("[HodProCompat] Hydrate or Diedrate's nutrition deficit HUD was not found yet.");
                    loggedMissing = true;
                }
                return false;
            }

            if (hodStatbarField == null || hodStatbarField.DeclaringType != hodHud.GetType())
            {
                Type hudType = hodHud.GetType();
                hodStatbarField = hudType.GetField("_statbar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (hodStatbarField == null)
                {
                    capi.Logger.Error("[HodProCompat] HoD's _statbar field is missing; HoD's HUD changed. Compat disabled.");
                    return false;
                }
                Type barType = hodStatbarField.FieldType;
                hodSetLineInterval = barType.GetMethod("SetCustomLineInterval");
                hodSetMinMax = barType.GetMethod("SetCustomMinMax");
                if (hodSetLineInterval == null || hodSetMinMax == null)
                {
                    capi.Logger.Error("[HodProCompat] HoD's custom statbar API changed. Compat disabled.");
                    hodStatbarField = null;
                    return false;
                }
            }

            hodStatbar = hodStatbarField.GetValue(hodHud);
            loggedMissing = false;
            return hodStatbar != null;
        }
    }
}
