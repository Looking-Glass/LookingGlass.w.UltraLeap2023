using System;

namespace LookingGlass.Blocks {
    /// <summary>
    /// <para>A struct that contains the args for a <c>createQuiltHologram</c> Blocks API call.</para>
    /// <para>See also: <seealso href="https://blocks.glass/api/graphql"/></para>
    /// </summary>
    [Serializable]
    public struct CreateQuiltHologramArgs {
        public static CreateQuiltHologramArgs Create1x1Args(string imageUrl, int fileSize) =>
            new CreateQuiltHologramArgs {
                title = "Dummy 1x1 Test Quilt",
                description = "This is a test 1x1 quilt.",
                imageUrl = imageUrl,
                width = 1,
                height = 1,
                type = HologramType.QUILT,
                fileSize = fileSize,
                aspectRatio = 0.75f,
                quiltCols = 1,
                quiltRows = 1,
                quiltTileCount = 1,
                isPublished = false
            };

        public string title;
        public string description;
        public string imageUrl;
        public int width;
        public int height;
        public HologramType type;
        public int fileSize;
        public float aspectRatio;
        public int quiltCols;
        public int quiltRows;
        public int quiltTileCount;
        public bool isPublished;
    }
}
