using System;
using System.Collections.Generic;

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongMaterialPage
    {
        public object OriginalTexture { get; }
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public SilksongMaterialPage(object originalTexture, string name, int width, int height)
        {
            OriginalTexture = originalTexture;
            Name = name;
            Width = width;
            Height = height;
        }
    }

    public static class SilksongCollectionAdmission
    {
        /// <summary>Admits one target texture of a collection: exact name and page count, and every page that
        /// draws the target's texture name has its exact original size and the same original texture object.
        /// Other pages may have any size (e.g. a 256x512 second page next to a 2048 first page).</summary>
        public static string Validate(SilksongSkinTarget target, string collectionName,
            IReadOnlyList<SilksongMaterialPage> pages)
        {
            if (target == null) return "Unknown collection target.";
            if (!string.Equals(target.CollectionName, collectionName, StringComparison.Ordinal))
                return "Exact collection name does not match the admitted target.";
            if (pages == null || pages.Count != target.MaterialCount)
                return "Exact material/page count does not match the admitted target.";
            object original = null;
            foreach (var page in pages)
            {
                if (page == null || page.OriginalTexture == null) return "A material page has no original texture.";
                if (!string.Equals(page.Name, target.TextureName, StringComparison.Ordinal)) continue;
                if (page.Width != target.Width || page.Height != target.Height)
                    return "Original atlas dimensions do not match the admitted target.";
                if (original == null) original = page.OriginalTexture;
                else if (!ReferenceEquals(original, page.OriginalTexture))
                    return "Pages named " + target.TextureName + " do not share the required shared original texture.";
            }
            return original == null ? "No material page draws " + target.TextureName + "." : null;
        }

        public static object Original(SilksongSkinTarget target, IReadOnlyList<SilksongMaterialPage> pages)
        {
            foreach (var page in pages)
                if (string.Equals(page.Name, target.TextureName, StringComparison.Ordinal)) return page.OriginalTexture;
            return null;
        }
    }
}
