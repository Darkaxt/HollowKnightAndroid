using System;
using System.Collections.Generic;

namespace DualSouls.Skins.Silksong.Runtime
{
    public sealed class SilksongMaterialPage
    {
        public object OriginalTexture { get; }
        public int Width { get; }
        public int Height { get; }
        public SilksongMaterialPage(object originalTexture, int width, int height)
        {
            OriginalTexture = originalTexture;
            Width = width;
            Height = height;
        }
    }

    public static class SilksongCollectionAdmission
    {
        public static string Validate(SilksongSkinTarget target, string collectionName,
            IReadOnlyList<SilksongMaterialPage> pages)
        {
            if (target == null) return "Unknown collection target.";
            if (!string.Equals(target.CollectionName, collectionName, StringComparison.Ordinal))
                return "Exact collection name does not match the admitted target.";
            if (pages == null || pages.Count != target.MaterialCount)
                return "Exact material/page count does not match the admitted target.";
            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                if (page == null || page.OriginalTexture == null ||
                    page.Width != target.Width || page.Height != target.Height)
                    return "Original atlas dimensions do not match the admitted target.";
            }
            if (target.RequiresSharedOriginal)
            {
                for (int i = 1; i < pages.Count; i++)
                    if (!ReferenceEquals(pages[0].OriginalTexture, pages[i].OriginalTexture))
                        return "Paired materials do not share the required shared original texture.";
            }
            return null;
        }
    }
}
