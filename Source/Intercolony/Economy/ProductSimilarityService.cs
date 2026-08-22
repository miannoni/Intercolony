using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Resolves how much product-specific reputation can carry from one ThingDef to another.
    ///
    /// This service owns only similarity. It deliberately does not read ProductBrandRecord,
    /// calculate brand, or influence pricing; keeping those concerns separate prevents a later
    /// consumer from silently making the reputation model depend on whichever caller reached it
    /// first (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md §4.4).
    /// </summary>
    public static class ProductSimilarityService
    {
        /// <summary>
        /// Even unrelated known-quality manufacturing retains a very small generalized-craft
        /// prestige. A non-zero floor avoids making specialization the only possible source of
        /// reputation while keeping the useful carryover overwhelmingly product-specific.
        /// </summary>
        public const float UnrelatedFloor = 0.05f;

        /// <summary>
        /// A shared broad category is meaningful, but it is not evidence that the same workshop
        /// specialty transfers. Thirty-five percent keeps this tier in the plan's 20-50% band.
        /// </summary>
        public const float SameBroadCategorySimilarity = 0.35f;

        /// <summary>
        /// Same-industry metadata transfers a substantial general capability without pretending
        /// that two product families are interchangeable. Seventy-five percent sits inside the
        /// plan's 60-90% band and leaves room for the narrow-family tier above it.
        /// </summary>
        public const float SameIndustrySimilarity = 0.75f;

        /// <summary>
        /// A shared narrow family category or explicit product-family tag is very strong evidence
        /// of transferable specialization. Ninety-five percent is deliberately inside the plan's
        /// 93-97% band, but is still distinct from exact ThingDef identity.
        /// </summary>
        public const float NarrowFamilySimilarity = 0.95f;

        /// <summary>
        /// The ceiling makes the bound explicit instead of relying on every future evidence rule
        /// to remember that similarity is a normalized carryover factor.
        /// </summary>
        public const float MaximumSimilarity = 1.0f;

        /// <summary>
        /// Exact identity is intentionally named separately from the ceiling so the self-product
        /// rule remains obvious when balance constants are retuned later.
        /// </summary>
        public const float ExactProductSimilarity = MaximumSimilarity;

        private static Dictionary<ThingDef, ProductProfile> profileCache =
            new Dictionary<ThingDef, ProductProfile>();

        /// <summary>
        /// The evidence branch selected by the resolver. The numeric value is intentionally kept
        /// on the service constants above so debug callers can report both the reason and the
        /// exact current balance without maintaining a second table.
        /// </summary>
        public enum ProductSimilarityEvidence
        {
            NullDefinitionFloor,
            ExactThingDef,
            SharedNarrowThingCategory,
            SharedProductMetadata,
            SameIndustryMetadata,
            SharedBroadThingCategory,
            SameIntercolonyCategory,
            UnrelatedFloor
        }

        /// <summary>
        /// Returns the normalized, symmetric carryover factor for two products.
        ///
        /// The first call for a def builds one immutable metadata profile. Subsequent calls only
        /// do dictionary lookups and bounded list scans; no LINQ, lambda, or per-call collection
        /// is created in the pricing/tooltip hot path (DESIGN.md §84).
        /// </summary>
        public static float GetSimilarity(ThingDef left, ThingDef right)
        {
            return Evaluate(left, right).Value;
        }

        /// <summary>
        /// Explains the selected evidence branch for a debug dump or future tooltip. This method
        /// allocates its human-readable text by design; callers that need the hot numeric path
        /// should use <see cref="GetSimilarity"/> instead.
        /// </summary>
        public static string Explain(ThingDef left, ThingDef right)
        {
            SimilarityResult result = Evaluate(left, right);
            StringBuilder sb = new StringBuilder();
            sb.Append("Product similarity ");
            sb.Append(DefName(left));
            sb.Append(" <-> ");
            sb.Append(DefName(right));
            sb.Append(" = ");
            sb.Append(result.Value.ToString("0.000"));
            sb.Append(" (");
            sb.Append((result.Value * 100f).ToString("0.0"));
            sb.AppendLine("%).");

            switch (result.Evidence)
            {
                case ProductSimilarityEvidence.NullDefinitionFloor:
                    sb.AppendLine(
                        "Evidence: unrelated floor. One or both ThingDefs is null, so no " +
                        "product evidence is available.");
                    break;
                case ProductSimilarityEvidence.ExactThingDef:
                    sb.AppendLine(
                        "Evidence: exact ThingDef identity. A product carries all of its own " +
                        "direct reputation to itself.");
                    break;
                case ProductSimilarityEvidence.SharedNarrowThingCategory:
                    sb.Append("Evidence: narrow family. Both defs share the nested ThingCategoryDef '");
                    sb.Append(result.SharedThingCategory?.defName ?? "unknown");
                    sb.AppendLine("; no display-name matching is involved.");
                    break;
                case ProductSimilarityEvidence.SharedProductMetadata:
                    sb.Append("Evidence: narrow family. Shared ");
                    sb.Append(result.MetadataKind);
                    sb.Append(" '");
                    sb.Append(result.SharedMetadata);
                    sb.AppendLine("' is explicit def metadata.");
                    break;
                case ProductSimilarityEvidence.SameIndustryMetadata:
                    sb.Append("Evidence: same industry. ");
                    if (result.SharedWeaponClass != null)
                    {
                        sb.Append("Both defs share weapon class '");
                        sb.Append(result.SharedWeaponClass.defName);
                        sb.AppendLine("'.");
                    }
                    else if (result.LeftProfile.IsWeapon && result.RightProfile.IsWeapon)
                    {
                        sb.AppendLine("Both defs are weapons, so general weapon-making capability transfers.");
                    }
                    else
                    {
                        sb.AppendLine("Both defs are apparel, so general apparel-making capability transfers.");
                    }
                    break;
                case ProductSimilarityEvidence.SharedBroadThingCategory:
                    sb.Append("Evidence: same broad RimWorld ThingCategoryDef '");
                    sb.Append(result.SharedThingCategory?.defName ?? "unknown");
                    sb.AppendLine("'. No narrower family evidence matched.");
                    break;
                case ProductSimilarityEvidence.SameIntercolonyCategory:
                    sb.Append("Evidence: same broad Intercolony category '");
                    sb.Append(result.SharedIntercolonyCategory?.Label() ?? "unknown");
                    sb.AppendLine("'. No narrower def evidence matched.");
                    break;
                default:
                    sb.AppendLine(
                        "Evidence: unrelated floor. The defs share no confirmed meaningful " +
                        "product evidence.");
                    break;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Clears derived profiles after an unusual def reload. Normal RimWorld sessions do not
        /// reload defs, but an explicit invalidation keeps this cache from becoming authoritative
        /// if a development tool changes the definition universe.
        /// </summary>
        public static void Invalidate()
        {
            profileCache.Clear();
        }

        private static SimilarityResult Evaluate(ThingDef left, ThingDef right)
        {
            if (left == null || right == null)
            {
                return SimilarityResult.Floor(
                    UnrelatedFloor, ProductSimilarityEvidence.NullDefinitionFloor);
            }

            if (ReferenceEquals(left, right))
            {
                return SimilarityResult.Exact();
            }

            ProductProfile leftProfile = GetProfile(left);
            ProductProfile rightProfile = GetProfile(right);

            // Only a genuinely nested category is narrow evidence. Core's WeaponsRanged and
            // BuildingsFurniture categories are both depth-two industry buckets, so promoting
            // either one to 95% would collapse broad industry evidence into a family match.
            if (TryFindSharedThingCategory(leftProfile, rightProfile, out ThingCategoryDef sharedCategory) &&
                IsNarrowThingCategoryMatch(sharedCategory))
            {
                return SimilarityResult.WithThingCategory(
                    NarrowFamilySimilarity,
                    ProductSimilarityEvidence.SharedNarrowThingCategory,
                    sharedCategory, leftProfile, rightProfile);
            }

            // Explicit tags are the def author's strongest signal when a mod supplies a family
            // that does not have a useful vanilla category. Compare only like-for-like metadata
            // collections: a building tag named "Production" must not accidentally match a
            // weapon tag carrying the same spelling.
            if (TrySharedString(leftProfile.WeaponTags, rightProfile.WeaponTags, out string sharedTag))
            {
                return SimilarityResult.WithMetadata(
                    NarrowFamilySimilarity,
                    "weapon tag", sharedTag, leftProfile, rightProfile);
            }

            if (TrySharedString(leftProfile.ApparelTags, rightProfile.ApparelTags, out sharedTag))
            {
                return SimilarityResult.WithMetadata(
                    NarrowFamilySimilarity,
                    "apparel tag", sharedTag, leftProfile, rightProfile);
            }

            if (TrySharedString(leftProfile.BuildingTags, rightProfile.BuildingTags, out sharedTag))
            {
                return SimilarityResult.WithMetadata(
                    NarrowFamilySimilarity,
                    "building tag", sharedTag, leftProfile, rightProfile);
            }

            // WeaponClassDef is meaningful same-industry metadata, but a class such as Ranged is
            // broader than a concrete family tag such as Gun. It therefore earns the middle band.
            if (TrySharedWeaponClass(
                    leftProfile.WeaponClasses, rightProfile.WeaponClasses,
                    out WeaponClassDef sharedWeaponClass))
            {
                return SimilarityResult.WithWeaponClass(
                    SameIndustrySimilarity, sharedWeaponClass, leftProfile, rightProfile);
            }

            if (leftProfile.IsWeapon && rightProfile.IsWeapon)
            {
                return SimilarityResult.Industry(
                    SameIndustrySimilarity, leftProfile, rightProfile);
            }

            if (leftProfile.IsApparel && rightProfile.IsApparel)
            {
                return SimilarityResult.Industry(
                    SameIndustrySimilarity, leftProfile, rightProfile);
            }

            // A shared category remains useful even when it is too broad to identify a narrow
            // specialty. This is the safe path for chairs/tables and for modded defs whose only
            // reliable relationship is a RimWorld storage category.
            if (sharedCategory != null &&
                CategoryDepth(sharedCategory) >= BroadThingCategoryMinimumDepth)
            {
                return SimilarityResult.WithThingCategory(
                    SameBroadCategorySimilarity,
                    ProductSimilarityEvidence.SharedBroadThingCategory,
                    sharedCategory, leftProfile, rightProfile);
            }

            IntercolonyProductCategory? leftCategory = ClassifySafely(left);
            IntercolonyProductCategory? rightCategory = ClassifySafely(right);
            if (leftCategory.HasValue && leftCategory == rightCategory)
            {
                return SimilarityResult.WithIntercolonyCategory(
                    SameBroadCategorySimilarity,
                    leftCategory.Value, leftProfile, rightProfile);
            }

            return SimilarityResult.Floor(
                UnrelatedFloor, ProductSimilarityEvidence.UnrelatedFloor,
                leftProfile, rightProfile);
        }

        private static IntercolonyProductCategory? ClassifySafely(ThingDef def)
        {
            try
            {
                return IntercolonyProductClassifier.Classify(def);
            }
            catch (Exception)
            {
                // A partially authored modded def should lose only its broad-category evidence,
                // not take down a tooltip. The earlier exact/category/tag branches remain useful,
                // and the final floor is the honest result when the classifier cannot inspect it.
                return null;
            }
        }

        private static ProductProfile GetProfile(ThingDef def)
        {
            if (profileCache.TryGetValue(def, out ProductProfile cached))
            {
                return cached;
            }

            ProductProfile created = new ProductProfile(def);
            profileCache.Add(def, created);
            return created;
        }

        // CategoryDepth counts Root as 0, its direct children as 1, and the first specific
        // product buckets (such as BuildingsFurniture and WeaponsRanged) as 2. Keep broad
        // evidence at that first specific level, while narrow evidence requires one more
        // authored family level beneath it.
        private const int BroadThingCategoryMinimumDepth = 2;
        private const int NarrowThingCategoryMinimumDepth = 3;

        private static bool IsNarrowThingCategoryMatch(ThingCategoryDef sharedCategory)
        {
            // A depth-three category is a conservative, definition-driven signal that a mod
            // authored a product family below RimWorld's broad industry bucket. The depth-two
            // WeaponsRanged category is intentionally left to weapon tags/classes and the
            // same-industry branch, which keeps a grenade or launcher from inheriting a firearm's
            // full specialization merely because both are ranged weapons.
            return sharedCategory != null &&
                   CategoryDepth(sharedCategory) >= NarrowThingCategoryMinimumDepth;
        }

        private static bool TryFindSharedThingCategory(
            ProductProfile left, ProductProfile right, out ThingCategoryDef shared)
        {
            shared = null;
            List<ThingCategoryDef> leftCategories = left.ThingCategories;
            List<ThingCategoryDef> rightCategories = right.ThingCategories;
            if (leftCategories == null || rightCategories == null ||
                leftCategories.Count == 0 || rightCategories.Count == 0)
            {
                return false;
            }

            int bestDepth = -1;
            bool bestWasDirect = false;
            for (int i = 0; i < leftCategories.Count; i++)
            {
                ThingCategoryDef leftDirect = leftCategories[i];
                for (ThingCategoryDef leftCandidate = leftDirect;
                     leftCandidate != null;
                     leftCandidate = leftCandidate.parent)
                {
                    int depth = CategoryDepth(leftCandidate);
                    for (int j = 0; j < rightCategories.Count; j++)
                    {
                        ThingCategoryDef rightDirect = rightCategories[j];
                        for (ThingCategoryDef rightCandidate = rightDirect;
                             rightCandidate != null;
                             rightCandidate = rightCandidate.parent)
                        {
                            if (!ReferenceEquals(leftCandidate, rightCandidate))
                            {
                                continue;
                            }

                            bool isDirect = ReferenceEquals(leftCandidate, leftDirect) &&
                                             ReferenceEquals(rightCandidate, rightDirect);
                            if (depth > bestDepth ||
                                (depth == bestDepth && isDirect && !bestWasDirect))
                            {
                                bestDepth = depth;
                                bestWasDirect = isDirect;
                                shared = leftCandidate;
                            }
                        }
                    }
                }
            }

            // Root itself is a taxonomy implementation detail, not evidence that two products
            // resemble one another. Requiring a parent also avoids a malformed def with only the
            // root category becoming a false positive for every other product.
            return shared != null && shared.parent != null;
        }

        private static int CategoryDepth(ThingCategoryDef category)
        {
            int depth = 0;
            for (ThingCategoryDef current = category;
                 current != null && current.parent != null;
                 current = current.parent)
            {
                depth++;
            }

            return depth;
        }

        private static bool TrySharedString(
            List<string> left, List<string> right, out string shared)
        {
            shared = null;
            if (left == null || right == null || left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                string candidate = left[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                for (int j = 0; j < right.Count; j++)
                {
                    if (StringComparer.Ordinal.Equals(candidate, right[j]) &&
                        (shared == null || StringComparer.Ordinal.Compare(candidate, shared) < 0))
                    {
                        shared = candidate;
                    }
                }
            }

            return shared != null;
        }

        private static bool TrySharedWeaponClass(
            List<WeaponClassDef> left, List<WeaponClassDef> right, out WeaponClassDef shared)
        {
            shared = null;
            if (left == null || right == null || left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                WeaponClassDef candidate = left[i];
                if (candidate == null)
                {
                    continue;
                }

                for (int j = 0; j < right.Count; j++)
                {
                    if (ReferenceEquals(candidate, right[j]) &&
                        (shared == null || StringComparer.Ordinal.Compare(
                            candidate.defName, shared.defName) < 0))
                    {
                        shared = candidate;
                    }
                }
            }

            return shared != null;
        }

        private static string DefName(ThingDef def)
        {
            return def == null || string.IsNullOrEmpty(def.defName)
                ? "<null/unnamed>"
                : def.defName;
        }

        private static float Bound(float value)
        {
            // Keep a corrupt future rule from violating the service contract. This is manual
            // rather than Mathf.Clamp so NaN cannot pass through as it would under comparisons.
            if (float.IsNaN(value))
            {
                return UnrelatedFloor;
            }

            if (value < UnrelatedFloor)
            {
                return UnrelatedFloor;
            }

            return value > MaximumSimilarity ? MaximumSimilarity : value;
        }

        private sealed class ProductProfile
        {
            public readonly List<ThingCategoryDef> ThingCategories;
            public readonly List<string> WeaponTags;
            public readonly List<WeaponClassDef> WeaponClasses;
            public readonly List<string> ApparelTags;
            public readonly List<string> BuildingTags;
            public readonly bool IsWeapon;
            public readonly bool IsApparel;

            public ProductProfile(ThingDef def)
            {
                // Keep references to def-owned lists. Defs are immutable after loading, so copying
                // them would only create cold-start garbage and a second source of truth.
                ThingCategories = def.thingCategories;
                WeaponTags = def.weaponTags;
                WeaponClasses = def.weaponClasses;
                ApparelTags = def.apparel?.tags;
                BuildingTags = def.building?.buildingTags;
                IsWeapon = def.IsWeapon;
                IsApparel = def.IsApparel;
            }
        }

        private readonly struct SimilarityResult
        {
            public readonly float Value;
            public readonly ProductSimilarityEvidence Evidence;
            public readonly ThingCategoryDef SharedThingCategory;
            public readonly string MetadataKind;
            public readonly string SharedMetadata;
            public readonly WeaponClassDef SharedWeaponClass;
            public readonly ProductProfile LeftProfile;
            public readonly ProductProfile RightProfile;
            public readonly IntercolonyProductCategory? SharedIntercolonyCategory;

            private SimilarityResult(
                float value,
                ProductSimilarityEvidence evidence,
                ThingCategoryDef sharedThingCategory,
                string metadataKind,
                string sharedMetadata,
                WeaponClassDef sharedWeaponClass,
                ProductProfile leftProfile,
                ProductProfile rightProfile,
                IntercolonyProductCategory? sharedIntercolonyCategory)
            {
                Value = Bound(value);
                Evidence = evidence;
                SharedThingCategory = sharedThingCategory;
                MetadataKind = metadataKind;
                SharedMetadata = sharedMetadata;
                SharedWeaponClass = sharedWeaponClass;
                LeftProfile = leftProfile;
                RightProfile = rightProfile;
                SharedIntercolonyCategory = sharedIntercolonyCategory;
            }

            public static SimilarityResult Exact()
            {
                return new SimilarityResult(
                    ExactProductSimilarity, ProductSimilarityEvidence.ExactThingDef,
                    null, null, null, null, null, null, null);
            }

            public static SimilarityResult Floor(
                float value, ProductSimilarityEvidence evidence,
                ProductProfile left = null, ProductProfile right = null)
            {
                return new SimilarityResult(
                    value, evidence, null, null, null, null, left, right, null);
            }

            public static SimilarityResult WithThingCategory(
                float value,
                ProductSimilarityEvidence evidence,
                ThingCategoryDef category,
                ProductProfile left,
                ProductProfile right)
            {
                return new SimilarityResult(
                    value, evidence, category, null, null, null, left, right, null);
            }

            public static SimilarityResult WithMetadata(
                float value,
                string metadataKind,
                string metadata,
                ProductProfile left,
                ProductProfile right)
            {
                return new SimilarityResult(
                    value, ProductSimilarityEvidence.SharedProductMetadata,
                    null, metadataKind, metadata, null, left, right, null);
            }

            public static SimilarityResult WithWeaponClass(
                float value,
                WeaponClassDef weaponClass,
                ProductProfile left,
                ProductProfile right)
            {
                return new SimilarityResult(
                    value, ProductSimilarityEvidence.SameIndustryMetadata,
                    null, null, null, weaponClass, left, right, null);
            }

            public static SimilarityResult Industry(
                float value, ProductProfile left, ProductProfile right)
            {
                return new SimilarityResult(
                    value, ProductSimilarityEvidence.SameIndustryMetadata,
                    null, null, null, null, left, right, null);
            }

            public static SimilarityResult WithIntercolonyCategory(
                float value,
                IntercolonyProductCategory category,
                ProductProfile left,
                ProductProfile right)
            {
                return new SimilarityResult(
                    value, ProductSimilarityEvidence.SameIntercolonyCategory,
                    null, null, null, null, left, right, category);
            }
        }
    }
}
