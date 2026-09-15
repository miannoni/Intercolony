using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    internal static class ProducePresetDesignators
    {
        private static Map cachedMap;
        private static int cachedPresetsRevision = -1;

        internal static void SyncFor(Map map)
        {
            if (map == null || map != Find.CurrentMap)
            {
                return;
            }

            ProduceLoopMapComponent loopComponent = ProduceLoopMapComponent.For(map);
            if (loopComponent == null)
            {
                return;
            }

            if (cachedMap == map && cachedPresetsRevision == loopComponent.PresetsRevision)
            {
                return;
            }

            DesignationCategoryDef category =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("IntercolonyProduction");
            if (category == null)
            {
                cachedMap = map;
                cachedPresetsRevision = loopComponent.PresetsRevision;
                return;
            }

            if (Find.DesignatorManager?.SelectedDesignator is Designator_ProducePreset)
            {
                Find.DesignatorManager.Deselect();
            }

            List<Designator> designators = category.AllResolvedDesignators;
            for (int i = designators.Count - 1; i >= 0; i--)
            {
                // Type-based removal also cleans stale preset designators left by a previous map or game.
                if (designators[i] is Designator_ProducePreset)
                {
                    designators.RemoveAt(i);
                }
            }

            IReadOnlyList<ProduceControlPreset> presets = loopComponent.Presets;
            if (presets != null)
            {
                for (int i = 0; i < presets.Count; i++)
                {
                    if (presets[i] != null)
                    {
                        designators.Add(new Designator_ProducePreset(presets[i]));
                    }
                }
            }

            cachedMap = map;
            cachedPresetsRevision = loopComponent.PresetsRevision;
        }

        internal static void Clear()
        {
            DesignationCategoryDef category =
                DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("IntercolonyProduction");
            if (category != null)
            {
                if (Find.DesignatorManager?.SelectedDesignator is Designator_ProducePreset)
                {
                    Find.DesignatorManager.Deselect();
                }

                List<Designator> designators = category.AllResolvedDesignators;
                for (int i = designators.Count - 1; i >= 0; i--)
                {
                    if (designators[i] is Designator_ProducePreset)
                    {
                        designators.RemoveAt(i);
                    }
                }
            }

            cachedMap = null;
            cachedPresetsRevision = -1;
        }

        internal static bool IsCachedFor(Map map)
        {
            // RimWorld changes CurrentMap before notifying components that the old map was removed.
            return map != null && cachedMap == map;
        }
    }
}
